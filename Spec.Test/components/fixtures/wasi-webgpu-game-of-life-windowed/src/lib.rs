// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Standalone windowed demo: Conway's Game of Life on the GPU,
// rendered to a wasi:surface window via the wasi:webgpu swap-
// chain path. Run with:
//   wacs run --wasi-gfx --windowed \
//     wasm/game-of-life-windowed.component.wasm
// Hit Escape (or close the window) to quit.

wit_bindgen::generate!({
    path: "wit",
    world: "game-of-life-windowed",
    generate_all,
});

export!(Demo);

use wasi::graphics_context::graphics_context;
use wasi::surface::surface;
use wasi::webgpu::webgpu;

// Combined WGSL: one compute entry plus a vertex+fragment pair
// for the cell-grid rasterization. Two storage buffers are
// ping-ponged each frame for the Conway transition; the
// fragment shader samples whichever buffer holds the just-
// computed "current" state.
const SHADER_WGSL: &str = r#"
const W: u32 = 64u;
const H: u32 = 64u;

@group(0) @binding(0) var<storage, read>       src: array<u32, 4096>;
@group(0) @binding(1) var<storage, read_write> dst: array<u32, 4096>;

fn at(x: i32, y: i32) -> u32 {
    let xw = u32((x + i32(W)) % i32(W));
    let yw = u32((y + i32(H)) % i32(H));
    return src[yw * W + xw];
}

@compute @workgroup_size(8, 8)
fn cs_main(@builtin(global_invocation_id) id: vec3<u32>) {
    if id.x >= W || id.y >= H { return; }
    let x = i32(id.x);
    let y = i32(id.y);
    var n: u32 = 0u;
    n += at(x - 1, y - 1); n += at(x, y - 1); n += at(x + 1, y - 1);
    n += at(x - 1, y);                          n += at(x + 1, y);
    n += at(x - 1, y + 1); n += at(x, y + 1); n += at(x + 1, y + 1);
    let alive = src[id.y * W + id.x];
    var next: u32;
    if alive == 1u {
        if n == 2u || n == 3u { next = 1u; } else { next = 0u; }
    } else {
        if n == 3u { next = 1u; } else { next = 0u; }
    }
    dst[id.y * W + id.x] = next;
}

struct CellUniforms {
    surface_w: f32,
    surface_h: f32,
};
@group(0) @binding(0) var<uniform> u: CellUniforms;
@group(0) @binding(1) var<storage, read> grid: array<u32, 4096>;

@vertex
fn vs_main(@builtin(vertex_index) vi: u32)
    -> @builtin(position) vec4<f32>
{
    var p = array<vec2<f32>, 3>(
        vec2(-1.0, -1.0),
        vec2( 3.0, -1.0),
        vec2(-1.0,  3.0),
    );
    return vec4<f32>(p[vi], 0.0, 1.0);
}

@fragment
fn fs_main(@builtin(position) fp: vec4<f32>) -> @location(0) vec4<f32> {
    let cw = u.surface_w / f32(W);
    let ch = u.surface_h / f32(H);
    let cx = u32(fp.x / cw);
    let cy = u32(fp.y / ch);
    if cx >= W || cy >= H {
        return vec4<f32>(0.0, 0.0, 0.0, 1.0);
    }
    let alive = grid[cy * W + cx];
    if alive == 1u {
        return vec4<f32>(0.86, 0.93, 1.00, 1.0);
    }
    return vec4<f32>(0.05, 0.07, 0.12, 1.0);
}
"#;

const W: usize = 64;
const H: usize = 64;
const N: usize = W * H;
const BYTES: u64 = (N * 4) as u64;

// WebGPU usage / map / shader-stage flag values (numeric — the
// WIT doesn't expose them as constants).
const BU_COPY_DST: u32 = 0x0008;
const BU_STORAGE: u32 = 0x0080;
const BU_UNIFORM: u32 = 0x0040;
const TU_RENDER_ATTACHMENT: u32 = 0x0010;
const SS_COMPUTE: u32 = 0x4;
const SS_FRAGMENT: u32 = 0x2;

// Seed with an R-pentomino — small, chaotic, evolves for ~1100
// generations on an infinite plane (we run on a torus so the
// trail eventually wraps and self-collides, which is fine for
// a screensaver-style demo).
fn initial_grid() -> Vec<u8> {
    let mut g = vec![0u32; N];
    let cx = W / 2;
    let cy = H / 2;
    // .XX
    // XX.
    // .X.
    g[(cy - 1) * W + (cx)]     = 1;
    g[(cy - 1) * W + (cx + 1)] = 1;
    g[(cy)     * W + (cx - 1)] = 1;
    g[(cy)     * W + (cx)]     = 1;
    g[(cy + 1) * W + (cx)]     = 1;
    g.iter().flat_map(|v| v.to_le_bytes()).collect()
}

struct Demo;

impl Guest for Demo {
    fn start() {
        // -------- window / surface --------
        let canvas = surface::Surface::new(surface::CreateDesc {
            width: Some(640),
            height: Some(640),
        });
        let ctx = graphics_context::Context::new();
        canvas.connect_graphics_context(&ctx);

        // -------- webgpu device --------
        let gpu = webgpu::get_gpu();
        let adapter = match gpu.request_adapter(None) {
            Some(a) => a,
            None => unreachable!("no adapter"),
        };
        let device = match adapter.request_device(None) {
            Ok(d) => d,
            Err(e) => unreachable!("request_device: {}", e.message),
        };
        let queue = device.queue();

        // Wire the graphics context to the device — this is
        // what configures the wgpu surface against the SDL
        // window's Metal layer.
        device.connect_graphics_context(&ctx);

        // -------- buffers --------
        let buf_a = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BYTES,
            usage: BU_STORAGE | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("grid-a".to_string()),
        });
        let buf_b = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BYTES,
            usage: BU_STORAGE | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("grid-b".to_string()),
        });
        let init = initial_grid();
        if let Err(e) =
            queue.write_buffer_with_copy(&buf_a, 0, &init, None, None)
        {
            unreachable!("write_buffer: {}", e.message);
        }

        // Uniforms (surface w/h as f32 pair, 8 bytes; pad to
        // 16-byte uniform-buffer alignment).
        let uniforms = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: 16,
            usage: BU_UNIFORM | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("uniforms".to_string()),
        });
        write_uniforms(&queue, &uniforms, 640.0, 640.0);

        // -------- shader + bind layouts + pipelines --------
        let shader =
            device.create_shader_module(&webgpu::GpuShaderModuleDescriptor {
                code: SHADER_WGSL.to_string(),
                compilation_hints: None,
                label: Some("gol-shader".to_string()),
            });

        // Compute bgl: two storage buffers (read + read_write).
        let cs_bgl = device.create_bind_group_layout(
            &webgpu::GpuBindGroupLayoutDescriptor {
                entries: vec![
                    bgl_entry_buffer(0, SS_COMPUTE,
                        webgpu::GpuBufferBindingType::ReadOnlyStorage),
                    bgl_entry_buffer(1, SS_COMPUTE,
                        webgpu::GpuBufferBindingType::Storage),
                ],
                label: Some("cs-bgl".to_string()),
            },
        );
        let cs_pl =
            device.create_pipeline_layout(&webgpu::GpuPipelineLayoutDescriptor {
                bind_group_layouts: vec![Some(&cs_bgl)],
                label: Some("cs-pl".to_string()),
            });
        let cs_pipeline = device.create_compute_pipeline(
            webgpu::GpuComputePipelineDescriptor {
                compute: webgpu::GpuProgrammableStage {
                    module: &shader,
                    entry_point: Some("cs_main".to_string()),
                    constants: None,
                },
                layout: webgpu::GpuLayoutMode::Specific(&cs_pl),
                label: Some("cs-pipeline".to_string()),
            },
        );

        // Render bgl: uniform (binding 0) + read-only storage
        // (binding 1, fragment-visible).
        let rs_bgl = device.create_bind_group_layout(
            &webgpu::GpuBindGroupLayoutDescriptor {
                entries: vec![
                    webgpu::GpuBindGroupLayoutEntry {
                        binding: 0,
                        visibility: SS_FRAGMENT,
                        buffer: Some(webgpu::GpuBufferBindingLayout {
                            type_: Some(webgpu::GpuBufferBindingType::Uniform),
                            has_dynamic_offset: None,
                            min_binding_size: None,
                        }),
                        sampler: None,
                        texture: None,
                        storage_texture: None,
                    },
                    bgl_entry_buffer(1, SS_FRAGMENT,
                        webgpu::GpuBufferBindingType::ReadOnlyStorage),
                ],
                label: Some("rs-bgl".to_string()),
            },
        );
        let rs_pl =
            device.create_pipeline_layout(&webgpu::GpuPipelineLayoutDescriptor {
                bind_group_layouts: vec![Some(&rs_bgl)],
                label: Some("rs-pl".to_string()),
            });
        let rs_pipeline = device.create_render_pipeline(
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
                        // wgpu's preferred surface format is
                        // typically Bgra8UnormSrgb on macOS Metal,
                        // but our wasi-webgpu Silk backend reports
                        // Bgra8unorm-srgb from the surface's
                        // GetPreferredFormat. Match that.
                        format: webgpu::GpuTextureFormat::Bgra8unormSrgb,
                        blend: None,
                        write_mask: None,
                    })],
                    module: &shader,
                    entry_point: Some("fs_main".to_string()),
                    constants: None,
                }),
                layout: webgpu::GpuLayoutMode::Specific(&rs_pl),
                label: Some("rs-pipeline".to_string()),
            },
        );

        // -------- compute bind groups (ping-pong) --------
        let cs_bg_ab = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &cs_bgl,
            entries: vec![
                bg_buffer_entry(0, &buf_a),
                bg_buffer_entry(1, &buf_b),
            ],
            label: Some("cs-bg-ab".to_string()),
        });
        let cs_bg_ba = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &cs_bgl,
            entries: vec![
                bg_buffer_entry(0, &buf_b),
                bg_buffer_entry(1, &buf_a),
            ],
            label: Some("cs-bg-ba".to_string()),
        });

        // -------- render bind groups (uniform + current grid) --
        let rs_bg_a = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &rs_bgl,
            entries: vec![
                bg_buffer_entry(0, &uniforms),
                bg_buffer_entry(1, &buf_a),
            ],
            label: Some("rs-bg-a".to_string()),
        });
        let rs_bg_b = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &rs_bgl,
            entries: vec![
                bg_buffer_entry(0, &uniforms),
                bg_buffer_entry(1, &buf_b),
            ],
            label: Some("rs-bg-b".to_string()),
        });

        // -------- frame loop --------
        let frame = canvas.subscribe_frame();
        let key_down = canvas.subscribe_key_down();
        let resize = canvas.subscribe_resize();
        let pollables = vec![&frame, &key_down, &resize];

        let mut tick: u64 = 0;
        loop {
            let ready = wasi::io::poll::poll(&pollables);

            // Key-down: quit on Escape.
            if ready.contains(&1) {
                while let Some(ev) = canvas.get_key_down() {
                    if let Some(k) = ev.key {
                        if matches!(k, surface::Key::Escape) {
                            return;
                        }
                    }
                }
            }

            // Resize: update the uniform so the fragment-side
            // cell-coord math stays correct.
            if ready.contains(&2) {
                if let Some(ev) = canvas.get_resize() {
                    write_uniforms(&queue, &uniforms,
                        ev.width as f32, ev.height as f32);
                }
            }

            if ready.contains(&0) {
                canvas.get_frame();
                let even = tick % 2 == 0;
                let (cs_bg, rs_bg) = if even {
                    (&cs_bg_ab, &rs_bg_b) // A→B; render B
                } else {
                    (&cs_bg_ba, &rs_bg_a) // B→A; render A
                };

                // Current swap-chain view for this frame.
                let ab = ctx.get_current_buffer();
                let tex = webgpu::GpuTexture::from_graphics_buffer(ab);
                let view = tex.create_view(None);

                let encoder = device.create_command_encoder(None);
                {
                    let p = encoder.begin_compute_pass(None);
                    p.set_pipeline(&cs_pipeline);
                    if let Err(e) =
                        p.set_bind_group(0, Some(cs_bg), None, None, None)
                    {
                        unreachable!("cs set_bind_group: {}", e.message);
                    }
                    p.dispatch_workgroups(1, None, None);
                    p.end();
                }
                {
                    let p = encoder.begin_render_pass(
                        &webgpu::GpuRenderPassDescriptor {
                            color_attachments: vec![Some(
                                webgpu::GpuRenderPassColorAttachment {
                                    view: &view,
                                    depth_slice: None,
                                    resolve_target: None,
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
                            label: Some("rs-pass".to_string()),
                        },
                    );
                    p.set_pipeline(&rs_pipeline);
                    if let Err(e) =
                        p.set_bind_group(0, Some(rs_bg), None, None, None)
                    {
                        unreachable!("rs set_bind_group: {}", e.message);
                    }
                    p.draw(3, None, None, None);
                    p.end();
                }
                let cmd = encoder.finish(None);
                queue.submit(&[&cmd]);

                // context.present routes through the wgpu
                // surface-present on the Silk backend.
                ctx.present();
                tick += 1;
            }
        }
    }
}

fn bgl_entry_buffer(
    binding: u32, vis: u32, ty: webgpu::GpuBufferBindingType,
) -> webgpu::GpuBindGroupLayoutEntry {
    webgpu::GpuBindGroupLayoutEntry {
        binding,
        visibility: vis,
        buffer: Some(webgpu::GpuBufferBindingLayout {
            type_: Some(ty),
            has_dynamic_offset: None,
            min_binding_size: None,
        }),
        sampler: None,
        texture: None,
        storage_texture: None,
    }
}

fn bg_buffer_entry<'a>(
    binding: u32, buf: &'a webgpu::GpuBuffer,
) -> webgpu::GpuBindGroupEntry<'a> {
    webgpu::GpuBindGroupEntry {
        binding,
        resource: webgpu::GpuBindingResource::GpuBufferBinding(
            webgpu::GpuBufferBinding {
                buffer: buf,
                offset: None,
                size: None,
            },
        ),
    }
}

fn write_uniforms(
    queue: &webgpu::GpuQueue, uniforms: &webgpu::GpuBuffer,
    w: f32, h: f32,
) {
    let mut data = [0u8; 16];
    data[0..4].copy_from_slice(&w.to_le_bytes());
    data[4..8].copy_from_slice(&h.to_le_bytes());
    if let Err(e) =
        queue.write_buffer_with_copy(uniforms, 0, &data, None, None)
    {
        unreachable!("write_buffer uniforms: {}", e.message);
    }
}
