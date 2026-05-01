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

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Wacs.WASI.Preview1.Test
{
    public class WasiTestsuiteData : IEnumerable<object[]>
    {
        public static readonly string[] Families = { "c", "rust", "assemblyscript" };

        public IEnumerator<object[]> GetEnumerator()
        {
            string suiteRoot = LocateTestsuiteRoot();
            var skipMap = LoadSkipMap();

            foreach (var family in Families)
            {
                var dir = Path.Combine(suiteRoot, "tests", family,
                    "testsuite", "wasm32-wasip1");
                if (!Directory.Exists(dir)) continue;

                foreach (var wasmPath in Directory.EnumerateFiles(dir, "*.wasm")
                             .OrderBy(p => p, StringComparer.Ordinal))
                {
                    var name = Path.GetFileNameWithoutExtension(wasmPath);
                    var testId = $"{family}/{name}";
                    var jsonPath = Path.ChangeExtension(wasmPath, ".json");
                    var skipReason = skipMap.TryGetValue(testId, out var reason)
                        ? reason : null;
                    yield return new object[]
                    {
                        new WasiTestCase(testId, family, name, wasmPath,
                            File.Exists(jsonPath) ? jsonPath : null,
                            skipReason),
                    };
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Walk up from the assembly's bin/Debug/net8.0/ to find the repo's
        // Spec.Test/wasi submodule. Done at runtime (not via MSBuild copy) so
        // the testsuite stays in-place — copying ~70 fixtures + their
        // fs-tests.dir trees would bloat the build for no benefit.
        internal static string LocateTestsuiteRoot()
        {
            var asmDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;
            var dir = new DirectoryInfo(asmDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName,
                    "Spec.Test", "wasi");
                if (Directory.Exists(Path.Combine(candidate, "tests")))
                    return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate Spec.Test/wasi submodule from " + asmDir);
        }

        private static Dictionary<string, string> LoadSkipMap()
        {
            var asmDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;
            var skipPath = Path.Combine(asmDir, "skip.json");
            if (!File.Exists(skipPath))
                return new Dictionary<string, string>();

            using var fs = File.OpenRead(skipPath);
            var list = JsonSerializer.Deserialize<SkipList>(fs);
            var map = new Dictionary<string, string>();
            if (list?.Skip == null) return map;
            foreach (var e in list.Skip)
                if (!string.IsNullOrEmpty(e.Test))
                    map[e.Test] = e.Reason ?? string.Empty;
            return map;
        }
    }

    public class WasiTestCase : Xunit.Abstractions.IXunitSerializable
    {
        public string TestId { get; private set; } = string.Empty;
        public string Family { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public string WasmPath { get; private set; } = string.Empty;
        public string? ManifestPath { get; private set; }
        public string? SkipReason { get; private set; }

        public WasiTestCase() { }

        public WasiTestCase(string testId, string family, string name,
            string wasmPath, string? manifestPath, string? skipReason)
        {
            TestId = testId;
            Family = family;
            Name = name;
            WasmPath = wasmPath;
            ManifestPath = manifestPath;
            SkipReason = skipReason;
        }

        public void Deserialize(Xunit.Abstractions.IXunitSerializationInfo info)
        {
            TestId = info.GetValue<string>(nameof(TestId));
            Family = info.GetValue<string>(nameof(Family));
            Name = info.GetValue<string>(nameof(Name));
            WasmPath = info.GetValue<string>(nameof(WasmPath));
            ManifestPath = info.GetValue<string>(nameof(ManifestPath));
            SkipReason = info.GetValue<string>(nameof(SkipReason));
        }

        public void Serialize(Xunit.Abstractions.IXunitSerializationInfo info)
        {
            info.AddValue(nameof(TestId), TestId);
            info.AddValue(nameof(Family), Family);
            info.AddValue(nameof(Name), Name);
            info.AddValue(nameof(WasmPath), WasmPath);
            info.AddValue(nameof(ManifestPath), ManifestPath);
            info.AddValue(nameof(SkipReason), SkipReason);
        }

        public override string ToString() => TestId;
    }
}
