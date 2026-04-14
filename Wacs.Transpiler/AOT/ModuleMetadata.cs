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
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Manages embedding and extracting module metadata in transpiled assemblies.
    ///
    /// Strategy: Embed the original .wasm binary as an assembly resource.
    /// At load time, the runtime can parse it with BinaryModuleParser to
    /// reconstruct the Module (types, imports, exports, memories, tables, globals,
    /// data segments, element segments, etc.) — then substitute function bodies
    /// with the transpiled IL methods.
    ///
    /// This avoids building a custom serializer for the deeply nested Module type graph.
    /// Future optimization: embed only the metadata sections (strip code section)
    /// to reduce assembly size.
    /// </summary>
    public static class ModuleMetadata
    {
        public const string WasmResourceName = "module.wasm";
        public const string ManifestResourceName = "module.manifest.json";

        /// <summary>
        /// Manifest describing the transpiled module — maps function indices
        /// to method names in the generated assembly.
        /// </summary>
        public class Manifest
        {
            public string ModuleName { get; set; } = "";
            public string Namespace { get; set; } = "";
            public string FunctionsTypeName { get; set; } = "";
            public List<FunctionEntry> Functions { get; set; } = new();
            public int TranspiledCount { get; set; }
            public int FallbackCount { get; set; }
        }

        public class FunctionEntry
        {
            public int Index { get; set; }
            public string MethodName { get; set; } = "";
            public bool IsTranspiled { get; set; }
            public string? ExportName { get; set; }
        }

        /// <summary>
        /// Serialize a manifest to JSON bytes for embedding.
        /// </summary>
        public static byte[] SerializeManifest(Manifest manifest)
        {
            return JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        /// <summary>
        /// Deserialize a manifest from a JSON stream.
        /// </summary>
        public static Manifest? DeserializeManifest(Stream stream)
        {
            return JsonSerializer.Deserialize<Manifest>(stream, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        /// <summary>
        /// Extract the embedded .wasm binary from a transpiled assembly.
        /// Returns null if not found.
        /// </summary>
        public static Stream? GetEmbeddedWasm(Assembly assembly)
        {
            // Look for the resource by suffix match (assembly name prefixed)
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(WasmResourceName))
                    return assembly.GetManifestResourceStream(name);
            }
            return null;
        }

        /// <summary>
        /// Extract the manifest from a transpiled assembly.
        /// Returns null if not found.
        /// </summary>
        public static Manifest? GetManifest(Assembly assembly)
        {
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(ManifestResourceName))
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream != null)
                        return DeserializeManifest(stream);
                }
            }
            return null;
        }
    }
}
