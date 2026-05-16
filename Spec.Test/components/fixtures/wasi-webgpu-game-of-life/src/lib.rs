// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Conway's Game of Life parity fixture for WACS.WASI.GFX.Webgpu.
// Seeds an 8×8 toroidal grid with a horizontal blinker, runs
// two compute-pass iterations ping-ponging between two storage
// buffers, and verifies the state returns to the initial
// pattern (a blinker has period 2). Any cell-by-cell mismatch
// or wgpu error traps via unreachable!().

wit_bindgen::generate!({
    path: "wit",
    world: "game-of-life",
    generate_all,
});

export!(Example);

use wasi::webgpu::webgpu;

// WGSL: one cell per u32 (0 dead, 1 alive). Toroidal
// neighborhood (8-connected, wraps at edges). Compute shader's
// workgroup size matches the grid for a single dispatch.
const SHADER_WGSL: &str = r#"
const W: u32 = 8u;
const H: u32 = 8u;

@group(0) @binding(0) var<storage, read>       src: array<u32, 64>;
@group(0) @binding(1) var<storage, read_write> dst: array<u32, 64>;

fn at(x: i32, y: i32) -> u32 {
    let xw = u32((x + i32(W)) % i32(W));
    let yw = u32((y + i32(H)) % i32(H));
    return src[yw * W + xw];
}

@compute @workgroup_size(8, 8)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    if id.x >= W || id.y >= H { return; }
    let x = i32(id.x);
    let y = i32(id.y);
    var n: u32 = 0u;
    n += at(x - 1, y - 1);
    n += at(x,     y - 1);
    n += at(x + 1, y - 1);
    n += at(x - 1, y);
    n += at(x + 1, y);
    n += at(x - 1, y + 1);
    n += at(x,     y + 1);
    n += at(x + 1, y + 1);
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

const W: usize = 8;
const H: usize = 8;
const N: usize = W * H; // 64 cells
const BYTES: u64 = (N * 4) as u64; // u32 per cell

// Horizontal blinker at row 3, columns 2..4. Three adjacent
// live cells in a horizontal line — a period-2 oscillator.
fn initial_grid() -> [u32; N] {
    let mut g = [0u32; N];
    let row = 3;
    g[row * W + 2] = 1;
    g[row * W + 3] = 1;
    g[row * W + 4] = 1;
    g
}

const BU_MAP_READ: u32 = 0x0001;
const BU_COPY_SRC: u32 = 0x0004;
const BU_COPY_DST: u32 = 0x0008;
const BU_STORAGE: u32 = 0x0080;
const SS_COMPUTE: u32 = 0x4;
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

        // Two ping-pong storage buffers: A and B.
        let buf_a = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BYTES,
            usage: BU_STORAGE | BU_COPY_SRC | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("grid-a".to_string()),
        });
        let buf_b = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BYTES,
            usage: BU_STORAGE | BU_COPY_SRC | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("grid-b".to_string()),
        });
        // Readback target (MAP_READ + COPY_DST).
        let staging = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BYTES,
            usage: BU_MAP_READ | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("staging".to_string()),
        });

        // Seed A with the initial grid.
        let init = initial_grid();
        let init_bytes: Vec<u8> = init
            .iter()
            .flat_map(|v| v.to_le_bytes())
            .collect();
        if let Err(e) =
            queue.write_buffer_with_copy(&buf_a, 0, &init_bytes, None, None)
        {
            unreachable!("write_buffer error: {}", e.message);
        }

        // Shader + bind-group-layout + pipeline-layout.
        let shader =
            device.create_shader_module(&webgpu::GpuShaderModuleDescriptor {
                code: SHADER_WGSL.to_string(),
                compilation_hints: None,
                label: Some("gol-kernel".to_string()),
            });
        let bgl = device.create_bind_group_layout(
            &webgpu::GpuBindGroupLayoutDescriptor {
                entries: vec![
                    webgpu::GpuBindGroupLayoutEntry {
                        binding: 0,
                        visibility: SS_COMPUTE,
                        buffer: Some(webgpu::GpuBufferBindingLayout {
                            type_: Some(webgpu::GpuBufferBindingType::ReadOnlyStorage),
                            has_dynamic_offset: None,
                            min_binding_size: None,
                        }),
                        sampler: None,
                        texture: None,
                        storage_texture: None,
                    },
                    webgpu::GpuBindGroupLayoutEntry {
                        binding: 1,
                        visibility: SS_COMPUTE,
                        buffer: Some(webgpu::GpuBufferBindingLayout {
                            type_: Some(webgpu::GpuBufferBindingType::Storage),
                            has_dynamic_offset: None,
                            min_binding_size: None,
                        }),
                        sampler: None,
                        texture: None,
                        storage_texture: None,
                    },
                ],
                label: Some("gol-bgl".to_string()),
            },
        );
        let pl =
            device.create_pipeline_layout(&webgpu::GpuPipelineLayoutDescriptor {
                bind_group_layouts: vec![Some(&bgl)],
                label: Some("gol-pl".to_string()),
            });

        // Two bind groups: bg_ab reads A and writes B; bg_ba
        // reads B and writes A.
        let bg_ab = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &bgl,
            entries: vec![
                webgpu::GpuBindGroupEntry {
                    binding: 0,
                    resource: webgpu::GpuBindingResource::GpuBufferBinding(
                        webgpu::GpuBufferBinding {
                            buffer: &buf_a,
                            offset: None,
                            size: None,
                        },
                    ),
                },
                webgpu::GpuBindGroupEntry {
                    binding: 1,
                    resource: webgpu::GpuBindingResource::GpuBufferBinding(
                        webgpu::GpuBufferBinding {
                            buffer: &buf_b,
                            offset: None,
                            size: None,
                        },
                    ),
                },
            ],
            label: Some("bg-ab".to_string()),
        });
        let bg_ba = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &bgl,
            entries: vec![
                webgpu::GpuBindGroupEntry {
                    binding: 0,
                    resource: webgpu::GpuBindingResource::GpuBufferBinding(
                        webgpu::GpuBufferBinding {
                            buffer: &buf_b,
                            offset: None,
                            size: None,
                        },
                    ),
                },
                webgpu::GpuBindGroupEntry {
                    binding: 1,
                    resource: webgpu::GpuBindingResource::GpuBufferBinding(
                        webgpu::GpuBufferBinding {
                            buffer: &buf_a,
                            offset: None,
                            size: None,
                        },
                    ),
                },
            ],
            label: Some("bg-ba".to_string()),
        });

        let pipeline = device.create_compute_pipeline(
            webgpu::GpuComputePipelineDescriptor {
                compute: webgpu::GpuProgrammableStage {
                    module: &shader,
                    entry_point: Some("main".to_string()),
                    constants: None,
                },
                layout: webgpu::GpuLayoutMode::Specific(&pl),
                label: Some("gol-pipeline".to_string()),
            },
        );

        // Encode two steps + copy A → staging.
        let encoder = device.create_command_encoder(None);
        {
            let pass = encoder.begin_compute_pass(None);
            pass.set_pipeline(&pipeline);
            if let Err(e) =
                pass.set_bind_group(0, Some(&bg_ab), None, None, None)
            {
                unreachable!("set_bind_group AB error: {}", e.message);
            }
            pass.dispatch_workgroups(1, None, None);
            pass.end();
        }
        {
            let pass = encoder.begin_compute_pass(None);
            pass.set_pipeline(&pipeline);
            if let Err(e) =
                pass.set_bind_group(0, Some(&bg_ba), None, None, None)
            {
                unreachable!("set_bind_group BA error: {}", e.message);
            }
            pass.dispatch_workgroups(1, None, None);
            pass.end();
        }
        encoder.copy_buffer_to_buffer(&buf_a, 0, &staging, 0, BYTES);
        let cmd = encoder.finish(None);
        queue.submit(&[&cmd]);

        // Map + verify the post-2-step state matches the
        // initial blinker (period-2 oscillator).
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

        if bytes.len() as u64 != BYTES {
            unreachable!(
                "readback length mismatch: got {} expected {}",
                bytes.len(), BYTES
            );
        }
        let mut result = [0u32; N];
        for i in 0..N {
            let b = &bytes[i * 4..i * 4 + 4];
            result[i] = u32::from_le_bytes([b[0], b[1], b[2], b[3]]);
        }
        let expected = initial_grid();
        let mut mismatches = 0u32;
        let mut first_bad: usize = 0;
        for i in 0..N {
            if result[i] != expected[i] {
                if mismatches == 0 { first_bad = i; }
                mismatches += 1;
            }
        }
        if mismatches != 0 {
            unreachable!(
                "GoL blinker after 2 steps differs from initial: \
                 {} cells mismatch (first at index {}: got {} expected {})",
                mismatches, first_bad,
                result[first_bad], expected[first_bad]
            );
        }
    }
}
