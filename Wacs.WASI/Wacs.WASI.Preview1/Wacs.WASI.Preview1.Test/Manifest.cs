// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Wacs.WASI.Preview1.Test
{
    // Mirrors the legacy wasi-testsuite per-test JSON manifest schema documented
    // at WebAssembly/wasi-testsuite/doc/specification.md. Operation-based
    // manifests (proposals/operations) aren't used by the wasm32-wasip1
    // testsuite branch we consume, so we don't model them.
    public class Manifest
    {
        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        [JsonPropertyName("env")]
        public Dictionary<string, string>? Env { get; set; }

        [JsonPropertyName("dirs")]
        public List<string>? Dirs { get; set; }

        [JsonPropertyName("exit_code")]
        public int? ExitCode { get; set; }

        [JsonPropertyName("stdout")]
        public string? Stdout { get; set; }

        [JsonPropertyName("stderr")]
        public string? Stderr { get; set; }
    }

    public class SkipEntry
    {
        [JsonPropertyName("test")]
        public string Test { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    public class SkipList
    {
        [JsonPropertyName("skip")]
        public List<SkipEntry> Skip { get; set; } = new();
    }
}
