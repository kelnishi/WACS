// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Standalone windowed demo: Conway's Game of Life on the GPU.
//
// Controls:
//   click+drag — paint live cells (sim pauses while dragging)
//   space      — toggle pause / resume
//   c          — clear grid
//   r          — randomize grid (each press reseeds)
//   escape     — quit

wit_bindgen::generate!({
    path: "wit",
    world: "game-of-life-windowed",
    generate_all,
});

export!(Demo);

use wasi::graphics_context::graphics_context;
use wasi::surface::surface;
use wasi::webgpu::webgpu;

// Two separate shader modules: WGSL doesn't allow two
// resource variables at the same @group/@binding within one
// module, even if they're used by disjoint entry points.

const CS_WGSL: &str = r#"
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
"#;

const RS_WGSL: &str = r#"
const W: u32 = 64u;
const H: u32 = 64u;

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

// WebGPU usage / map / shader-stage flag values.
const BU_COPY_DST: u32 = 0x0008;
const BU_STORAGE: u32 = 0x0080;
const BU_UNIFORM: u32 = 0x0040;
const SS_COMPUTE: u32 = 0x4;
const SS_FRAGMENT: u32 = 0x2;

// xorshift32 — small, no dep needed.
fn random_grid(seed: u32) -> Vec<u8> {
    let mut s = seed | 1;
    let mut g = vec![0u32; N];
    for cell in g.iter_mut() {
        s ^= s << 13;
        s ^= s >> 17;
        s ^= s << 5;
        // ~28% alive density — chaotic but not saturated.
        *cell = if (s & 0xFF) < 72 { 1 } else { 0 };
    }
    g.iter().flat_map(|v| v.to_le_bytes()).collect()
}

fn empty_grid() -> Vec<u8> {
    vec![0u8; (BYTES) as usize]
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
        // Initial random seed.
        let mut seed_counter: u32 = 0x12345678;
        let init = random_grid(seed_counter);
        write_both(&queue, &buf_a, &buf_b, &init);

        let uniforms = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: 16,
            usage: BU_UNIFORM | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("uniforms".to_string()),
        });
        let mut win_w: f32 = 640.0;
        let mut win_h: f32 = 640.0;
        write_uniforms(&queue, &uniforms, win_w, win_h);

        // -------- shaders + pipelines --------
        let cs_shader =
            device.create_shader_module(&webgpu::GpuShaderModuleDescriptor {
                code: CS_WGSL.to_string(),
                compilation_hints: None,
                label: Some("cs-shader".to_string()),
            });
        let rs_shader =
            device.create_shader_module(&webgpu::GpuShaderModuleDescriptor {
                code: RS_WGSL.to_string(),
                compilation_hints: None,
                label: Some("rs-shader".to_string()),
            });

        let cs_bgl = device.create_bind_group_layout(
            &webgpu::GpuBindGroupLayoutDescriptor {
                entries: vec![
                    bgl_buffer(0, SS_COMPUTE,
                        webgpu::GpuBufferBindingType::ReadOnlyStorage),
                    bgl_buffer(1, SS_COMPUTE,
                        webgpu::GpuBufferBindingType::Storage),
                ],
                label: Some("cs-bgl".to_string()),
            },
        );
        let cs_pl = device.create_pipeline_layout(
            &webgpu::GpuPipelineLayoutDescriptor {
                bind_group_layouts: vec![Some(&cs_bgl)],
                label: Some("cs-pl".to_string()),
            },
        );
        let cs_pipeline = device.create_compute_pipeline(
            webgpu::GpuComputePipelineDescriptor {
                compute: webgpu::GpuProgrammableStage {
                    module: &cs_shader,
                    entry_point: Some("cs_main".to_string()),
                    constants: None,
                },
                layout: webgpu::GpuLayoutMode::Specific(&cs_pl),
                label: Some("cs-pipeline".to_string()),
            },
        );

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
                    bgl_buffer(1, SS_FRAGMENT,
                        webgpu::GpuBufferBindingType::ReadOnlyStorage),
                ],
                label: Some("rs-bgl".to_string()),
            },
        );
        let rs_pl = device.create_pipeline_layout(
            &webgpu::GpuPipelineLayoutDescriptor {
                bind_group_layouts: vec![Some(&rs_bgl)],
                label: Some("rs-pl".to_string()),
            },
        );
        let rs_pipeline = device.create_render_pipeline(
            webgpu::GpuRenderPipelineDescriptor {
                vertex: webgpu::GpuVertexState {
                    buffers: None,
                    module: &rs_shader,
                    entry_point: Some("vs_main".to_string()),
                    constants: None,
                },
                primitive: None,
                depth_stencil: None,
                multisample: None,
                fragment: Some(webgpu::GpuFragmentState {
                    targets: vec![Some(webgpu::GpuColorTargetState {
                        format: webgpu::GpuTextureFormat::Bgra8unormSrgb,
                        blend: None,
                        write_mask: None,
                    })],
                    module: &rs_shader,
                    entry_point: Some("fs_main".to_string()),
                    constants: None,
                }),
                layout: webgpu::GpuLayoutMode::Specific(&rs_pl),
                label: Some("rs-pipeline".to_string()),
            },
        );

        // Compute + render bind groups. Two each for the
        // ping-pong: bg_a* reads buf_a (the "current" when
        // current_is_a is true); bg_b* reads buf_b.
        let cs_bg_a_to_b = device.create_bind_group(
            &webgpu::GpuBindGroupDescriptor {
                layout: &cs_bgl,
                entries: vec![
                    bg_buffer(0, &buf_a),
                    bg_buffer(1, &buf_b),
                ],
                label: Some("cs-bg-a-to-b".to_string()),
            },
        );
        let cs_bg_b_to_a = device.create_bind_group(
            &webgpu::GpuBindGroupDescriptor {
                layout: &cs_bgl,
                entries: vec![
                    bg_buffer(0, &buf_b),
                    bg_buffer(1, &buf_a),
                ],
                label: Some("cs-bg-b-to-a".to_string()),
            },
        );
        let rs_bg_a = device.create_bind_group(
            &webgpu::GpuBindGroupDescriptor {
                layout: &rs_bgl,
                entries: vec![
                    bg_buffer(0, &uniforms),
                    bg_buffer(1, &buf_a),
                ],
                label: Some("rs-bg-a".to_string()),
            },
        );
        let rs_bg_b = device.create_bind_group(
            &webgpu::GpuBindGroupDescriptor {
                layout: &rs_bgl,
                entries: vec![
                    bg_buffer(0, &uniforms),
                    bg_buffer(1, &buf_b),
                ],
                label: Some("rs-bg-b".to_string()),
            },
        );

        // -------- event subscriptions --------
        let frame = canvas.subscribe_frame();
        let key_down = canvas.subscribe_key_down();
        let resize = canvas.subscribe_resize();
        let p_down = canvas.subscribe_pointer_down();
        let p_up = canvas.subscribe_pointer_up();
        let p_move = canvas.subscribe_pointer_move();
        let pollables = vec![
            &frame, &key_down, &resize, &p_down, &p_up, &p_move,
        ];
        const IDX_FRAME: u32 = 0;
        const IDX_KEY:   u32 = 1;
        const IDX_RESIZE: u32 = 2;
        const IDX_PDOWN: u32 = 3;
        const IDX_PUP:   u32 = 4;
        const IDX_PMOVE: u32 = 5;

        // -------- state --------
        let mut current_is_a: bool = true; // buf_a holds the current grid
        let mut paused: bool = false;
        let mut painting: bool = false;

        loop {
            let ready = wasi::io::poll::poll(&pollables);

            // ---- keyboard ----
            if ready.contains(&IDX_KEY) {
                while let Some(ev) = canvas.get_key_down() {
                    if let Some(k) = ev.key {
                        match k {
                            surface::Key::Escape => return,
                            surface::Key::Space  => paused = !paused,
                            surface::Key::KeyC   => {
                                let empty = empty_grid();
                                write_both(&queue, &buf_a, &buf_b, &empty);
                            }
                            surface::Key::KeyR   => {
                                seed_counter = seed_counter
                                    .wrapping_mul(0x9E3779B1)
                                    .wrapping_add(1);
                                let g = random_grid(seed_counter);
                                write_both(&queue, &buf_a, &buf_b, &g);
                            }
                            _ => {}
                        }
                    }
                }
            }

            // ---- mouse ----
            if ready.contains(&IDX_PDOWN) {
                while let Some(ev) = canvas.get_pointer_down() {
                    painting = true;
                    paint_at(&queue, &buf_a, &buf_b,
                        ev.x, ev.y, win_w, win_h);
                }
            }
            if ready.contains(&IDX_PUP) {
                while canvas.get_pointer_up().is_some() {
                    painting = false;
                }
            }
            if ready.contains(&IDX_PMOVE) {
                while let Some(ev) = canvas.get_pointer_move() {
                    if painting {
                        paint_at(&queue, &buf_a, &buf_b,
                            ev.x, ev.y, win_w, win_h);
                    }
                }
            }

            // ---- resize ----
            if ready.contains(&IDX_RESIZE) {
                if let Some(ev) = canvas.get_resize() {
                    win_w = ev.width as f32;
                    win_h = ev.height as f32;
                    write_uniforms(&queue, &uniforms, win_w, win_h);
                }
            }

            // ---- frame ----
            if ready.contains(&IDX_FRAME) {
                canvas.get_frame();

                let ab = ctx.get_current_buffer();
                let tex = webgpu::GpuTexture::from_graphics_buffer(ab);
                let view = tex.create_view(None);

                let encoder = device.create_command_encoder(None);

                // Step the sim only if running and not painting.
                let do_step = !paused && !painting;
                if do_step {
                    let cs_bg = if current_is_a {
                        &cs_bg_a_to_b // src = A; dst = B
                    } else {
                        &cs_bg_b_to_a // src = B; dst = A
                    };
                    let p = encoder.begin_compute_pass(None);
                    p.set_pipeline(&cs_pipeline);
                    if let Err(e) =
                        p.set_bind_group(0, Some(cs_bg), None, None, None)
                    {
                        unreachable!("cs set_bind_group: {}", e.message);
                    }
                    p.dispatch_workgroups(1, None, None);
                    p.end();
                    current_is_a = !current_is_a;
                }

                let rs_bg = if current_is_a { &rs_bg_a } else { &rs_bg_b };
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
                ctx.present();
            }
        }
    }
}

fn bgl_buffer(
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

fn bg_buffer<'a>(
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

// Write the same grid bytes to BOTH ping-pong buffers so the
// next compute step reads the painted state regardless of which
// buffer is currently the "source".
fn write_both(
    queue: &webgpu::GpuQueue,
    buf_a: &webgpu::GpuBuffer,
    buf_b: &webgpu::GpuBuffer,
    bytes: &[u8],
) {
    if let Err(e) =
        queue.write_buffer_with_copy(buf_a, 0, bytes, None, None)
    {
        unreachable!("write_buffer A: {}", e.message);
    }
    if let Err(e) =
        queue.write_buffer_with_copy(buf_b, 0, bytes, None, None)
    {
        unreachable!("write_buffer B: {}", e.message);
    }
}

fn paint_at(
    queue: &webgpu::GpuQueue,
    buf_a: &webgpu::GpuBuffer,
    buf_b: &webgpu::GpuBuffer,
    x: f64, y: f64, win_w: f32, win_h: f32,
) {
    if win_w <= 0.0 || win_h <= 0.0 { return; }
    let cw = win_w as f64 / W as f64;
    let ch = win_h as f64 / H as f64;
    if cw <= 0.0 || ch <= 0.0 { return; }
    let cx = (x / cw).floor() as i64;
    let cy = (y / ch).floor() as i64;
    if cx < 0 || cy < 0 || cx >= W as i64 || cy >= H as i64 { return; }
    let idx = (cy as u64) * (W as u64) + (cx as u64);
    let offset = idx * 4;
    let bytes = 1u32.to_le_bytes();
    if let Err(e) =
        queue.write_buffer_with_copy(buf_a, offset, &bytes, None, None)
    {
        unreachable!("paint A: {}", e.message);
    }
    if let Err(e) =
        queue.write_buffer_with_copy(buf_b, offset, &bytes, None, None)
    {
        unreachable!("paint B: {}", e.message);
    }
}
