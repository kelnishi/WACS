// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Phase 5 of the init-data codec work: end-to-end round-trip that proves
    /// a saved <c>.dll</c> reconstructs its <see cref="ModuleInitData"/> from
    /// the embedded codec bytes when loaded into a fresh process state — the
    /// "cross-process" path the in-process transpile suite doesn't exercise.
    ///
    /// <para>Simulates a fresh process by wiping <see cref="InitRegistry"/>
    /// and <see cref="ModuleInit"/> before loading the saved assembly. Under
    /// those conditions, <see cref="InitializationHelper.InitializeFromEmbedded"/>
    /// must fall through to the codec-decode branch and rebuild everything —
    /// memories, data segments, globals, type hashes — from the embedded
    /// resource. No InitRegistry fast path.</para>
    ///
    /// <para>Scope: self-contained wasm modules (no imports, no GC init
    /// values, no try_table / tags). Modules with imports go through the
    /// <c>TranspiledModuleLoader</c> API that lands in phase 5b.</para>
    /// </summary>
    public class CrossProcessLoadTests : IDisposable
    {
        private readonly string _tempDir;

        public CrossProcessLoadTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "wacs-xp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        // =================================================================
        // Fixtures — hand-encoded minimal wasm modules.
        // Keeping them inline (no file dependency) makes the tests portable
        // and self-describing. The byte sequences follow the WebAssembly
        // binary format (§5 of the core spec).
        // =================================================================

        /// <summary>
        /// A no-import module with one export "add" taking (i32, i32) → i32.
        /// No memory, no data segments, no globals — smallest path that
        /// exercises the codec round-trip end to end.
        /// </summary>
        private static byte[] BuildAddWasm()
        {
            // (module
            //   (type (func (param i32 i32) (result i32)))
            //   (func (type 0) local.get 0 local.get 1 i32.add)
            //   (export "add" (func 0)))
            return new byte[]
            {
                // Magic + version
                0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
                // Type section: 1 type, (i32 i32) -> i32
                0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
                // Function section: 1 function, type 0
                0x03, 0x02, 0x01, 0x00,
                // Export section: 1 export "add" func 0
                0x07, 0x07, 0x01, 0x03, 0x61, 0x64, 0x64, 0x00, 0x00,
                // Code section: 1 function body
                // body: no locals, local.get 0, local.get 1, i32.add, end
                0x0A, 0x09, 0x01, 0x07, 0x00, 0x20, 0x00, 0x20, 0x01, 0x6A, 0x0B,
            };
        }

        /// <summary>
        /// A module with a memory + data segment — exercises the
        /// cross-process data-segment remap path specifically.
        /// Export "read" returns the byte at offset 0 as an i32.
        /// </summary>
        private static byte[] BuildMemoryWasm()
        {
            // (module
            //   (memory 1)
            //   (data (i32.const 0) "\2A")
            //   (func (result i32) i32.const 0 i32.load8_u)
            //   (export "read" (func 0)))
            return new byte[]
            {
                0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
                // Type section: () -> i32
                0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
                // Function section: 1 function
                0x03, 0x02, 0x01, 0x00,
                // Memory section: 1 memory, min=1
                0x05, 0x03, 0x01, 0x00, 0x01,
                // Export section: "read" func 0
                0x07, 0x08, 0x01, 0x04, 0x72, 0x65, 0x61, 0x64, 0x00, 0x00,
                // Code section: 1 body
                //   no locals, i32.const 0, i32.load8_u align=0 offset=0, end
                0x0A, 0x09, 0x01, 0x07, 0x00, 0x41, 0x00, 0x2D, 0x00, 0x00, 0x0B,
                // Data section: 1 segment, memory 0, (i32.const 0), 1 byte 0x2A
                0x0B, 0x07, 0x01, 0x00, 0x41, 0x00, 0x0B, 0x01, 0x2A,
            };
        }

        // =================================================================
        // Tests
        // =================================================================

        [Fact]
        public void AddModule_CrossProcess_RoundTrip()
        {
            var dllPath = Path.Combine(_tempDir, "add.dll");
            TranspileAndSave(BuildAddWasm(), dllPath, @namespace: "Wacs.Xp.AddTest");

            // Simulate a fresh process: wipe the registries the in-process
            // fast path reads from. After this the loaded assembly must
            // rebuild everything from its embedded codec bytes.
            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var asm = LoadIsolated(dllPath);
            var moduleType = asm.GetType("Wacs.Xp.AddTest.WasmModule.Module", throwOnError: true)!;
            var instance = Activator.CreateInstance(moduleType)!;
            var addMethod = moduleType.GetMethod("add")!;

            int result = (int)addMethod.Invoke(instance, new object[] { 7, 35 })!;
            Assert.Equal(42, result);
        }

        [Fact]
        public void MemoryModule_CrossProcess_DataSegmentRemap()
        {
            var dllPath = Path.Combine(_tempDir, "memory.dll");
            TranspileAndSave(BuildMemoryWasm(), dllPath, @namespace: "Wacs.Xp.MemTest");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var asm = LoadIsolated(dllPath);
            var moduleType = asm.GetType("Wacs.Xp.MemTest.WasmModule.Module", throwOnError: true)!;
            var instance = Activator.CreateInstance(moduleType)!;
            var readMethod = moduleType.GetMethod("read")!;

            int b = (int)readMethod.Invoke(instance, Array.Empty<object>())!;
            Assert.Equal(0x2A, b);
        }

        [Fact]
        public void MemoryModule_SameProcessTranspileReload_Works()
        {
            // Round-trip twice: first transpile + save + wipe + reload,
            // then wipe again + reload once more. Exercises repeated
            // cross-process loads of the same .dll in the same process.
            var dllPath = Path.Combine(_tempDir, "memory-repeat.dll");
            TranspileAndSave(BuildMemoryWasm(), dllPath, @namespace: "Wacs.Xp.RepeatTest");

            for (int round = 0; round < 2; round++)
            {
                InitRegistry.Reset();
                ModuleInit.Reset();
                MultiReturnMethodRegistry.Reset();

                var asm = LoadIsolated(dllPath, contextName: "repeat-" + round);
                var moduleType = asm.GetType("Wacs.Xp.RepeatTest.WasmModule.Module", throwOnError: true)!;
                var instance = Activator.CreateInstance(moduleType)!;
                int b = (int)moduleType.GetMethod("read")!.Invoke(instance, Array.Empty<object>())!;
                Assert.Equal(0x2A, b);
            }
        }

        [Fact]
        public void CodecDecode_RebuildsActiveDataSegments_WithFreshIds()
        {
            // White-box: transpile + save + wipe. Then feed the generated
            // assembly's embedded bytes through the codec directly and
            // InitializeFromData to verify the data-segment remap logic
            // works without the IL's Ldsfld indirection.
            var dllPath = Path.Combine(_tempDir, "codec-direct.dll");
            TranspileAndSave(BuildMemoryWasm(), dllPath, @namespace: "Wacs.Xp.DirectTest");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var asm = LoadIsolated(dllPath);
            var initType = asm.GetType("Wacs.Xp.DirectTest.WasmModule.__WACSInit", throwOnError: true)!;
            var dataField = initType.GetField("Data", BindingFlags.Public | BindingFlags.Static)!;
            var embedded = (byte[])dataField.GetValue(null)!;
            Assert.NotEmpty(embedded);

            var decoded = InitDataCodec.Decode(embedded);
            Assert.Single(decoded.Memories);
            Assert.Equal(1L, decoded.Memories[0].min);
            Assert.Single(decoded.ActiveDataSegments);
            Assert.Single(decoded.SavedDataSegments);

            // Every ActiveDataSegments entry's segId must resolve in
            // SavedDataSegments — if the codec round-trip mismatched the
            // IDs, cross-process memory init would break silently.
            foreach (var (_, _, segId) in decoded.ActiveDataSegments)
                Assert.True(decoded.SavedDataSegments.ContainsKey(segId),
                    $"ActiveDataSegments references segId={segId} but SavedDataSegments lacks it");
        }

        // =================================================================
        // Helpers
        // =================================================================

        /// <summary>
        /// Parse, transpile, and persist a wasm byte sequence to a .dll on
        /// disk. Mirrors what a consumer would do with
        /// <c>ModuleTranspiler.Transpile + result.SaveAssembly</c>.
        /// </summary>
        private static void TranspileAndSave(byte[] wasmBytes, string dllPath, string @namespace)
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(wasmBytes);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var transpiler = new ModuleTranspiler(@namespace, new TranspilerOptions());
            var result = transpiler.Transpile(moduleInst, runtime, "WasmModule");
            result.SaveAssembly(dllPath);
            Assert.True(File.Exists(dllPath), $"Expected {dllPath} after SaveAssembly");
        }

        /// <summary>
        /// Load the assembly into a fresh, collectible
        /// <see cref="AssemblyLoadContext"/>. Ensures the test exercises
        /// real Assembly.LoadFrom-ish semantics, not just a cached hit on
        /// the default context.
        /// </summary>
        private static Assembly LoadIsolated(string path, string? contextName = null)
        {
            var ctx = new AssemblyLoadContext(contextName ?? "wacs-xp-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                                               isCollectible: true);
            // Read into a byte[] first so the file is releasable immediately.
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            return ctx.LoadFromStream(ms);
        }
    }
}
