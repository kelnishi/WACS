// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Verifies <see cref="TranspilerOptions.AssemblyName"/> is honored
    /// verbatim — needed for the wacs-aot whole-program build path, where
    /// a saved <c>.dll</c> is statically referenced from a consumer csproj
    /// and ILC's resolver requires file name == assembly name.
    /// </summary>
    public class StableAssemblyNameTests
    {
        [Fact]
        public void DefaultBehaviorAppendsUniqueIdSuffix()
        {
            var (modInst, runtime) = ParseAndInstantiate(BuildAddWasm());
            var transpiler = new ModuleTranspiler("Wacs.Test.NameDefault", new TranspilerOptions());
            var result = transpiler.Transpile(modInst, runtime, "AddMod");

            var name = result.Assembly.GetName().Name!;
            Assert.StartsWith("Wacs.Test.NameDefault.AddMod_", name);
            // The suffix is a process-unique counter; just confirm one's there.
            Assert.True(name.Length > "Wacs.Test.NameDefault.AddMod_".Length,
                $"expected counter suffix on {name}");
        }

        [Fact]
        public void StableAssemblyNameOptionUsedVerbatim()
        {
            var (modInst, runtime) = ParseAndInstantiate(BuildAddWasm());
            var opts = new TranspilerOptions { AssemblyName = "MyCompany.MyApp" };
            var transpiler = new ModuleTranspiler("ignored.namespace", opts);
            var result = transpiler.Transpile(modInst, runtime, "ignored");

            Assert.Equal("MyCompany.MyApp", result.Assembly.GetName().Name);
        }

        [Fact]
        public void StableNameSurvivesSaveAssembly()
        {
            var (modInst, runtime) = ParseAndInstantiate(BuildAddWasm());
            var opts = new TranspilerOptions { AssemblyName = "MyCompany.SavedApp" };
            var transpiler = new ModuleTranspiler("ignored", opts);
            var result = transpiler.Transpile(modInst, runtime, "ignored");

            var dllPath = Path.Combine(Path.GetTempPath(),
                $"{result.Assembly.GetName().Name}.dll");
            try
            {
                result.SaveAssembly(dllPath);
                Assert.True(File.Exists(dllPath));

                // Re-read the persisted .dll's logical name from its bytes —
                // confirms Lokad.ILPack preserved the override end-to-end.
                var asmName = System.Reflection.AssemblyName.GetAssemblyName(dllPath);
                Assert.Equal("MyCompany.SavedApp", asmName.Name);
            }
            finally
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
            }
        }

        // Same minimal "(func (export "add") (param i32 i32) (result i32) ...)"
        // module used in CrossProcessLoadTests — no imports, no memory, no
        // data segments. Smallest valid input that exercises the transpiler
        // end-to-end.
        private static byte[] BuildAddWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x03, 0x02, 0x01, 0x00,
            0x07, 0x07, 0x01, 0x03, 0x61, 0x64, 0x64, 0x00, 0x00,
            0x0A, 0x09, 0x01, 0x07, 0x00, 0x20, 0x00, 0x20, 0x01, 0x6A, 0x0B,
        };

        private static (ModuleInstance, WasmRuntime) ParseAndInstantiate(byte[] wasm)
        {
            using var ms = new MemoryStream(wasm);
            var module = BinaryModuleParser.ParseWasm(ms);
            var runtime = new WasmRuntime();
            var modInst = runtime.InstantiateModule(module);
            return (modInst, runtime);
        }
    }
}
