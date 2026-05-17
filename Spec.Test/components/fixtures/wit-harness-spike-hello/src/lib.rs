// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Minimal component for the WIT-harness AOT spike. Single
// `greet(name) -> string` export over primitives only. Used
// by the harness spike to verify NativeAOT + Unity IL2CPP
// can run a typed harness against a real component binary.

wit_bindgen::generate!({
    path: "wit",
    world: "hello",
});

struct Example;

impl Guest for Example {
    fn greet(name: String) -> String {
        format!("Hello, {}!", name)
    }
}

export!(Example);
