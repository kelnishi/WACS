// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Render-path parity fixture for WACS.WASI.GFX.Webgpu. Draws a
// triangle to a 64×64 offscreen texture, copies it to a readback
// buffer, maps + verifies pixel colors. Traps on mismatch.

wit_bindgen::generate!({
    path: "wit",
    world: "hello-render",
    generate_all,
});

export!(Example);

use wasi::webgpu::webgpu;

// WGSL: vertex shader produces a triangle from
// @builtin(vertex_index); fragment outputs solid red. No
// uniforms / bind groups — keeps the bind-group-layout decode
// path off the critical path for this fixture.
const SHADER_WGSL: &str = r#"
@vertex
fn vs_main(@builtin(vertex_index) vi: u32) -> @builtin(position) vec4<f32> {
    var positions = array<vec2<f32>, 3>(
        vec2<f32>(-0.7, -0.7),
        vec2<f32>( 0.7, -0.7),
        vec2<f32>( 0.0,  0.7),
    );
    return vec4<f32>(positions[vi], 0.0, 1.0);
}

@fragment
fn fs_main() -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.0, 0.0, 1.0);
}
"#;

const WIDTH: u32 = 64;
const HEIGHT: u32 = 64;
// 64 px × 4 bytes/px = 256 bytes, already the wgpu
// COPY_BYTES_PER_ROW_ALIGNMENT — no row padding needed.
const BYTES_PER_ROW: u32 = WIDTH * 4;
const READBACK_BYTES: u64 = (BYTES_PER_ROW as u64) * (HEIGHT as u64);

// WebGPU buffer-usage flags.
const BU_MAP_READ: u32 = 0x0001;
const BU_COPY_DST: u32 = 0x0008;

// WebGPU texture-usage flags.
const TU_COPY_SRC: u32 = 0x0001;
const TU_RENDER_ATTACHMENT: u32 = 0x0010;

// Map-mode flags.
const MAP_READ: u32 = 0x0001;

struct Example;

impl Guest for Example {
    fn start() {
        let gpu = webgpu::get_gpu();
        let adapter = match gpu.request_adapter(None) {
            Some(a) => a,
            None => unreachable!("request_adapter returned None"),
        };
        let device = match adapter.request_device(None) {
            Ok(d) => d,
            Err(e) => unreachable!("request_device error: {}", e.message),
        };
        let queue = device.queue();

        // ----- shader module -----
        let shader =
            device.create_shader_module(&webgpu::GpuShaderModuleDescriptor {
                code: SHADER_WGSL.to_string(),
                compilation_hints: None,
                label: Some("triangle-shader".to_string()),
            });

        // ----- render pipeline (auto layout, RGBA8Unorm target) -----
        let pipeline = device.create_render_pipeline(
            webgpu::GpuRenderPipelineDescriptor {
                vertex: webgpu::GpuVertexState {
                    buffers: None,
                    module: &shader,
                    entry_point: Some("vs_main".to_string()),
                    constants: None,
                },
                primitive: None,
                depth_stencil: None,
                multisample: None,
                fragment: Some(webgpu::GpuFragmentState {
                    targets: vec![Some(webgpu::GpuColorTargetState {
                        // RGBA8 unorm matches the texture format we
                        // render to + read back.
                        format: webgpu::GpuTextureFormat::Rgba8unorm,
                        blend: None,
                        write_mask: None,
                    })],
                    module: &shader,
                    entry_point: Some("fs_main".to_string()),
                    constants: None,
                }),
                layout: webgpu::GpuLayoutMode::Auto,
                label: Some("triangle-pipeline".to_string()),
            },
        );

        // ----- offscreen color target -----
        let color_target = device.create_texture(&webgpu::GpuTextureDescriptor {
            size: webgpu::GpuExtent3D {
                width: WIDTH,
                height: Some(HEIGHT),
                depth_or_array_layers: Some(1),
            },
            mip_level_count: Some(1),
            sample_count: Some(1),
            dimension: Some(webgpu::GpuTextureDimension::D2),
            format: webgpu::GpuTextureFormat::Rgba8unorm,
            usage: TU_COPY_SRC | TU_RENDER_ATTACHMENT,
            view_formats: None,
            label: Some("color-target".to_string()),
        });
        let color_view = color_target.create_view(None);

        // ----- readback buffer -----
        let staging = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: READBACK_BYTES,
            usage: BU_MAP_READ | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("staging".to_string()),
        });

        // ----- record -----
        let encoder = device.create_command_encoder(None);
        {
            let pass = encoder.begin_render_pass(
                &webgpu::GpuRenderPassDescriptor {
                    color_attachments: vec![Some(
                        webgpu::GpuRenderPassColorAttachment {
                            view: &color_view,
                            depth_slice: None,
                            resolve_target: None,
                            // Clear to black.
                            clear_value: Some(webgpu::GpuColor {
                                r: 0.0, g: 0.0, b: 0.0, a: 1.0,
                            }),
                            load_op: webgpu::GpuLoadOp::Clear,
                            store_op: webgpu::GpuStoreOp::Store,
                        },
                    )],
                    depth_stencil_attachment: None,
                    occlusion_query_set: None,
                    timestamp_writes: None,
                    max_draw_count: None,
                    label: Some("render-pass".to_string()),
                },
            );
            pass.set_pipeline(&pipeline);
            pass.draw(3, None, None, None);
            pass.end();
        }
        encoder.copy_texture_to_buffer(
            &webgpu::GpuTexelCopyTextureInfo {
                texture: &color_target,
                mip_level: None,
                origin: None,
                aspect: None,
            },
            &webgpu::GpuTexelCopyBufferInfo {
                buffer: &staging,
                offset: None,
                bytes_per_row: Some(BYTES_PER_ROW),
                rows_per_image: Some(HEIGHT),
            },
            webgpu::GpuExtent3D {
                width: WIDTH,
                height: Some(HEIGHT),
                depth_or_array_layers: Some(1),
            },
        );
        let cmd = encoder.finish(None);
        queue.submit(&[&cmd]);

        // ----- map + verify -----
        if let Err(e) = staging.map_async(MAP_READ, None, None) {
            unreachable!("map_async error: {}", e.message);
        }
        let bytes = match staging.get_mapped_range_get_with_copy(None, None) {
            Ok(b) => b,
            Err(e) => unreachable!("get_mapped_range error: {}", e.message),
        };
        if let Err(e) = staging.unmap() {
            unreachable!("unmap error: {}", e.message);
        }

        if bytes.len() as u64 != READBACK_BYTES {
            unreachable!(
                "readback length mismatch: got {} expected {}",
                bytes.len(),
                READBACK_BYTES
            );
        }

        // The triangle in clip space spans (-0.7, -0.7) → (0.7, -0.7)
        // → (0.0, 0.7). In a 64×64 viewport that maps roughly to
        // pixels (10..54). The center pixel (32, 32) is inside;
        // pixel (5, 5) is outside (cleared to black).
        let center = sample_rgba(&bytes, 32, 32);
        if !is_red(center) {
            unreachable!(
                "center pixel (32,32) not red: rgba=({},{},{},{})",
                center.0, center.1, center.2, center.3
            );
        }
        let corner = sample_rgba(&bytes, 5, 5);
        if !is_black(corner) {
            unreachable!(
                "corner pixel (5,5) not black: rgba=({},{},{},{})",
                corner.0, corner.1, corner.2, corner.3
            );
        }
    }
}

fn sample_rgba(bytes: &[u8], x: u32, y: u32) -> (u8, u8, u8, u8) {
    let stride = BYTES_PER_ROW as usize;
    let i = (y as usize) * stride + (x as usize) * 4;
    (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3])
}

fn is_red(c: (u8, u8, u8, u8)) -> bool {
    c.0 >= 240 && c.1 <= 10 && c.2 <= 10 && c.3 >= 240
}

fn is_black(c: (u8, u8, u8, u8)) -> bool {
    c.0 <= 5 && c.1 <= 5 && c.2 <= 5 && c.3 >= 240
}
