// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Parity fixture for the WACS.WASI.GFX.Webgpu compute path.
// Runs an "add one" compute kernel over a u32 array end-to-
// end against a real wgpu backend and verifies the readback.
// Any expectation mismatch traps via unreachable!() — the
// host test runner reads the trap as the failure signal.

wit_bindgen::generate!({
    path: "wit",
    world: "hello-compute",
    generate_all,
});

export!(Example);

use wasi::webgpu::webgpu;

// add-one kernel over a flat u32 storage buffer. workgroup
// size 8 matches the 8-element input below so a single
// dispatch covers the whole array.
const SHADER_WGSL: &str = r#"
@group(0) @binding(0) var<storage, read_write> data: array<u32>;

@compute @workgroup_size(8)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    let i = id.x;
    if i < arrayLength(&data) {
        data[i] = data[i] + 1u;
    }
}
"#;

const N: usize = 8;
const BUFFER_BYTES: u64 = (N * 4) as u64;

// WebGPU buffer-usage flags. Values match the spec's
// GPUBufferUsage bit field; wgpu-native uses the same bits.
const BU_MAP_READ: u32 = 0x0001;
const BU_COPY_SRC: u32 = 0x0004;
const BU_COPY_DST: u32 = 0x0008;
const BU_STORAGE: u32 = 0x0080;

// Shader-stage flags.
const SS_COMPUTE: u32 = 0x4;

// Map-mode flags (MapMode::Read = 1).
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

        // Storage buffer: input/output for the kernel. STORAGE
        // for shader access; COPY_DST for queue.write_buffer to
        // seed the input; COPY_SRC so we can copy the result
        // out to the readback buffer.
        let storage = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BUFFER_BYTES,
            usage: BU_STORAGE | BU_COPY_SRC | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("storage".to_string()),
        });

        // Readback buffer: MAP_READ so we can map for read after
        // submit; COPY_DST so the encoder can copy the storage
        // buffer into it. MAP_READ + COPY_DST is the only
        // combination wgpu allows on MAP_READ buffers.
        let staging = device.create_buffer(&webgpu::GpuBufferDescriptor {
            size: BUFFER_BYTES,
            usage: BU_MAP_READ | BU_COPY_DST,
            mapped_at_creation: None,
            label: Some("staging".to_string()),
        });

        // Seed input: [1, 2, 3, ..., 8].
        let input_bytes = {
            let mut v = Vec::with_capacity(N * 4);
            for i in 0..N {
                let value = (i as u32) + 1;
                v.extend_from_slice(&value.to_le_bytes());
            }
            v
        };
        if let Err(e) =
            queue.write_buffer_with_copy(&storage, 0, &input_bytes, None, None)
        {
            unreachable!("write_buffer error: {}", e.message);
        }

        let shader =
            device.create_shader_module(&webgpu::GpuShaderModuleDescriptor {
                code: SHADER_WGSL.to_string(),
                compilation_hints: None,
                label: Some("kernel".to_string()),
            });

        let bgl =
            device.create_bind_group_layout(&webgpu::GpuBindGroupLayoutDescriptor {
                entries: vec![webgpu::GpuBindGroupLayoutEntry {
                    binding: 0,
                    visibility: SS_COMPUTE,
                    buffer: Some(webgpu::GpuBufferBindingLayout {
                        type_: Some(webgpu::GpuBufferBindingType::Storage),
                        has_dynamic_offset: None,
                        min_binding_size: None,
                    }),
                    sampler: None,
                    texture: None,
                    storage_texture: None,
                }],
                label: Some("bgl".to_string()),
            });

        let pl =
            device.create_pipeline_layout(&webgpu::GpuPipelineLayoutDescriptor {
                bind_group_layouts: vec![Some(&bgl)],
                label: Some("pl".to_string()),
            });

        let bg = device.create_bind_group(&webgpu::GpuBindGroupDescriptor {
            layout: &bgl,
            entries: vec![webgpu::GpuBindGroupEntry {
                binding: 0,
                resource: webgpu::GpuBindingResource::GpuBufferBinding(
                    webgpu::GpuBufferBinding {
                        buffer: &storage,
                        offset: None,
                        size: None,
                    },
                ),
            }],
            label: Some("bg".to_string()),
        });

        let pipeline = device.create_compute_pipeline(
            webgpu::GpuComputePipelineDescriptor {
                compute: webgpu::GpuProgrammableStage {
                    module: &shader,
                    entry_point: Some("main".to_string()),
                    constants: None,
                },
                layout: webgpu::GpuLayoutMode::Specific(&pl),
                label: Some("pipeline".to_string()),
            },
        );

        let encoder = device.create_command_encoder(None);
        {
            let pass = encoder.begin_compute_pass(None);
            pass.set_pipeline(&pipeline);
            if let Err(e) =
                pass.set_bind_group(0, Some(&bg), None, None, None)
            {
                unreachable!("set_bind_group error: {}", e.message);
            }
            // Workgroup size is 8; one workgroup covers all 8
            // elements.
            pass.dispatch_workgroups(1, None, None);
            pass.end();
        }
        encoder.copy_buffer_to_buffer(&storage, 0, &staging, 0, BUFFER_BYTES);
        let cmd = encoder.finish(None);
        queue.submit(&[&cmd]);

        // Map the staging buffer for reading; the host's
        // wgpu-poll bridge drives the callback to completion
        // synchronously, so map_async returns once the readback
        // is ready.
        if let Err(e) = staging.map_async(MAP_READ, None, None) {
            unreachable!("map_async error: {}", e.message);
        }
        let result_bytes = match staging.get_mapped_range_get_with_copy(None, None) {
            Ok(b) => b,
            Err(e) => unreachable!("get_mapped_range error: {}", e.message),
        };
        if let Err(e) = staging.unmap() {
            unreachable!("unmap error: {}", e.message);
        }

        // Verify: each input value should be incremented by 1,
        // so the readback is [2, 3, ..., 9].
        if result_bytes.len() != N * 4 {
            unreachable!(
                "result length mismatch: got {} expected {}",
                result_bytes.len(),
                N * 4
            );
        }
        for i in 0..N {
            let bytes = &result_bytes[i * 4..i * 4 + 4];
            let value = u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]);
            let expected = (i as u32) + 2;
            if value != expected {
                unreachable!(
                    "compute readback mismatch at index {}: got {} expected {}",
                    i, value, expected
                );
            }
        }
    }
}
