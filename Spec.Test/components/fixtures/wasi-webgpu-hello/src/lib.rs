// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// v1 phase 3 session 10 parity fixture for WACS.WASI.GFX.Webgpu.
// Exercises the wired bindings that work against the host's stub
// backend today; traps on expectation mismatch so a successful
// `start()` return signals all wire-form decoding landed
// correctly.

wit_bindgen::generate!({
    path: "wit",
    world: "hello",
    generate_all,
});

export!(Example);

use wasi::webgpu::webgpu;

struct Example;

impl Guest for Example {
    fn start() {
        // get-gpu() — allocates a fresh gpu handle each call,
        // backed by the singleton IGpu the host backend mints
        // exactly once.
        let gpu = webgpu::get_gpu();

        // gpu.get-preferred-canvas-format() — primitive enum
        // return through a single i32 wire result. The WACS
        // stub + Silk SilkGpu both report Bgra8UnormSrgb; reject
        // anything else as a wire-form regression.
        let format = gpu.get_preferred_canvas_format();
        if !matches!(format, webgpu::GpuTextureFormat::Bgra8unormSrgb) {
            // Trap on mismatch — the host's test runner reads
            // the trap as a failure signal.
            unreachable!("expected Bgra8unormSrgb");
        }

        // gpu.wgsl-language-features() — returns an own<handle>
        // for the WGSL feature set. The stub backend doesn't
        // advertise any features, so .has("anything") returns
        // false. The exercise here is the resource-handle
        // round-trip + string-param decode at the host side.
        let features = gpu.wgsl_language_features();
        if features.has("readonly_and_readwrite_storage_textures") {
            unreachable!("stub backend should report no WGSL features");
        }

        // resource-drop fires on the `gpu` + `features` handles
        // when they go out of scope. WACS reclaims the slots in
        // host.Gpus / host.WgslLanguageFeatures.
    }
}
