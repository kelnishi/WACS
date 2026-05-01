// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Verifies <see cref="EmissionTarget.AotLinked"/> emission produces a
    /// Module class that runs equivalently to the Standard emission, and
    /// that its saved .dll has no references to the
    /// <c>InitializationHelper.InitializeFromEmbedded</c> codec wrapper —
    /// the trimmer-evidence that NativeAOT can dead-strip the codec
    /// machinery from the final native binary.
    /// </summary>
    public class AotLinkedEmissionTests
    {
        [Fact]
        public void AotLinkedAndStandardEmissionsAgreeOnAddResult()
        {
            var (modInst, runtime) = ParseAndInstantiate(BuildAddWasm());

            var stdResult = new ModuleTranspiler("Wacs.AotLink.Std",
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(modInst, runtime, "AddMod");

            var (modInst2, runtime2) = ParseAndInstantiate(BuildAddWasm());
            var aotResult = new ModuleTranspiler("Wacs.AotLink.Aot",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst2, runtime2, "AddMod");

            int stdSum = (int)Invoke(stdResult, "add", 7, 35);
            int aotSum = (int)Invoke(aotResult, "add", 7, 35);

            Assert.Equal(42, stdSum);
            Assert.Equal(42, aotSum);
        }

        [Fact]
        public void AotLinkedSavedDllOmitsCodecHolderType()
        {
            var (modInst, runtime) = ParseAndInstantiate(BuildAddWasm());
            var opts = new TranspilerOptions
            {
                Emission = EmissionTarget.AotLinked,
                AssemblyName = "Wacs.AotLink.Trim",
            };
            var result = new ModuleTranspiler("ignored", opts)
                .Transpile(modInst, runtime, "AddMod");

            var dllPath = Path.Combine(Path.GetTempPath(), $"{result.Assembly.GetName().Name}.dll");
            try
            {
                result.SaveAssembly(dllPath);
                Assert.True(File.Exists(dllPath));

                // Two trimmer-evidence checks against the persisted .dll bytes:
                //   1) No __WACSInit holder type (we never called EmitEmbeddedInitData).
                //   2) No InitializeFromEmbedded reference (we used `new ThinContext()` instead).
                var bytes = File.ReadAllBytes(dllPath);
                var asUtf8 = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.DoesNotContain("__WACSInit", asUtf8);
                Assert.DoesNotContain("InitializeFromEmbedded", asUtf8);
            }
            finally
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
            }
        }

        [Fact]
        public void AotLinkedSupportsMemoryAndActiveDataSegment()
        {
            // Module with (memory 1) + data segment "*" at offset 0,
            // exporting `read` returning the byte at offset 0.
            // AotLinked must allocate the memory and copy the data
            // segment in the ctor so the export sees 0x2A.
            var (modInst, runtime) = ParseAndInstantiate(BuildMemoryWasm());

            var result = new ModuleTranspiler("Wacs.AotLink.Mem",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "MemMod");

            int read = (int)Invoke(result, "read");
            Assert.Equal(0x2A, read);
        }

        [Fact]
        public void AotLinkedThrowsOnModuleWithGlobals()
        {
            // Globals still aren't supported by the AotLinked ctor —
            // confirm the feasibility check still trips on them.
            var (modInst, runtime) = ParseAndInstantiate(BuildGlobalWasm());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                new ModuleTranspiler("Wacs.AotLink.GlobReject",
                    new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                    .Transpile(modInst, runtime, "GlobMod"));

            Assert.Contains("AotLinked", ex.Message);
            Assert.Contains("globals", ex.Message);
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static object Invoke(TranspilationResult result, string exportName, params object[] args)
        {
            var instance = Activator.CreateInstance(result.ModuleClass!)
                ?? throw new Exception("Activator returned null");
            var em = result.ExportMethods.First(m => m.WasmName == exportName);
            var method = result.ModuleClass!.GetMethod(em.Name,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new Exception($"export {em.Name} not found");
            try
            {
                return method.Invoke(instance, args)!;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        // (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add)
        private static byte[] BuildAddWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x03, 0x02, 0x01, 0x00,
            0x07, 0x07, 0x01, 0x03, 0x61, 0x64, 0x64, 0x00, 0x00,
            0x0A, 0x09, 0x01, 0x07, 0x00, 0x20, 0x00, 0x20, 0x01, 0x6A, 0x0B,
        };

        // (module
        //   (global $g i32 (i32.const 99))
        //   (func (export "read") (result i32) global.get $g))
        private static byte[] BuildGlobalWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // global: 1 global, i32 const 99
            0x06, 0x06, 0x01, 0x7F, 0x00, 0x41, 0x63, 0x0B,
            // export: "read" -> func 0
            0x07, 0x08, 0x01, 0x04, 0x72, 0x65, 0x61, 0x64, 0x00, 0x00,
            // code: global.get 0; end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x23, 0x00, 0x0B,
        };

        // (module
        //   (memory 1)
        //   (data (i32.const 0) "\2A")
        //   (func (export "read") (result i32) i32.const 0 i32.load8_u))
        private static byte[] BuildMemoryWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // memory: 1 mem, min=1
            0x05, 0x03, 0x01, 0x00, 0x01,
            // export: "read" -> func 0
            0x07, 0x08, 0x01, 0x04, 0x72, 0x65, 0x61, 0x64, 0x00, 0x00,
            // code: i32.const 0; i32.load8_u align=0 offset=0; end
            0x0A, 0x09, 0x01, 0x07, 0x00, 0x41, 0x00, 0x2D, 0x00, 0x00, 0x0B,
            // data: 1 segment, mem 0, (i32.const 0), 1 byte 0x2A
            0x0B, 0x07, 0x01, 0x00, 0x41, 0x00, 0x0B, 0x01, 0x2A,
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
