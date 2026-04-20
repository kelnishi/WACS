// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.Hosting;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Tests for <see cref="TranspiledModuleLoader"/> — the seamless-load
    /// API for consumers of saved transpiled assemblies. Covers both the
    /// no-import path (simplest) and the import paths (typed object + by-
    /// name delegate dictionary).
    /// </summary>
    public class TranspiledModuleLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public TranspiledModuleLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "wacs-loader-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // =================================================================
        // Fixtures
        // =================================================================

        /// <summary>No-import, (i32 i32) → i32 "add".</summary>
        private static byte[] BuildAddWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            0x03, 0x02, 0x01, 0x00,
            0x07, 0x07, 0x01, 0x03, 0x61, 0x64, 0x64, 0x00, 0x00,
            0x0A, 0x09, 0x01, 0x07, 0x00, 0x20, 0x00, 0x20, 0x01, 0x6A, 0x0B,
        };

        /// <summary>Module with an imported (env.multiply i32 i32 → i32)
        /// function plus an exported "call_mul" that invokes it.</summary>
        private static byte[] BuildImportModuleWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // Type section: 1 type — (i32 i32) → i32
            0x01, 0x07, 0x01, 0x60, 0x02, 0x7F, 0x7F, 0x01, 0x7F,
            // Import section: 1 import — env.multiply : type 0
            // size = 1 (count) + 1+3 (mod name) + 1+8 (import name) + 2 (desc) = 16
            0x02, 0x10, 0x01,
            0x03, 0x65, 0x6E, 0x76,                            // "env"
            0x08, 0x6D, 0x75, 0x6C, 0x74, 0x69, 0x70, 0x6C, 0x79, // "multiply"
            0x00, 0x00,                                          // func, type 0
            // Function section: 1 local function — type 0
            0x03, 0x02, 0x01, 0x00,
            // Export section: "call_mul" func 1 (imports come before locals)
            0x07, 0x0C, 0x01,
            0x08, 0x63, 0x61, 0x6C, 0x6C, 0x5F, 0x6D, 0x75, 0x6C, // "call_mul"
            0x00, 0x01,                                            // func, idx 1
            // Code: body calls import func 0 with its two params
            // local.get 0; local.get 1; call 0; end
            0x0A, 0x0A, 0x01, 0x08, 0x00, 0x20, 0x00, 0x20, 0x01, 0x10, 0x00, 0x0B,
        };

        // =================================================================
        // No-import path
        // =================================================================

        [Fact]
        public void Load_NoImports_ExposesExportsAndInvokes()
        {
            var dllPath = TranspileAndSave(BuildAddWasm(), "Wacs.Loader.Add");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var loaded = TranspiledModuleLoader.Load(dllPath);
            Assert.NotNull(loaded.ExportsInterface);
            Assert.Null(loaded.ImportsInterface); // no-import module
            Assert.Contains(loaded.Exports, m => m.Name == "add");

            int result = (int)loaded.Invoke("add", 2, 5)!;
            Assert.Equal(7, result);
        }

        [Fact]
        public void GetExport_TypedDelegate_Works()
        {
            var dllPath = TranspileAndSave(BuildAddWasm(), "Wacs.Loader.AddDel");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var loaded = TranspiledModuleLoader.Load(dllPath);
            var add = loaded.GetExport<Func<int, int, int>>("add");
            Assert.Equal(42, add(12, 30));
            Assert.Equal(-5, add(0, -5));
        }

        [Fact]
        public void Load_InspectsModuleShape()
        {
            // Reflection surface — a tool / consumer interrogates the
            // module before binding. Asserts the interfaces are real
            // CLR types, methods are real MethodInfos, etc.
            var dllPath = TranspileAndSave(BuildAddWasm(), "Wacs.Loader.Shape");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var loaded = TranspiledModuleLoader.Load(dllPath);
            Assert.True(loaded.ModuleType.IsClass);
            Assert.True(loaded.ExportsInterface!.IsInterface);
            var addExport = loaded.Exports.Single(m => m.Name == "add");
            Assert.Equal(typeof(int), addExport.ReturnType);
            Assert.Equal(2, addExport.GetParameters().Length);
            Assert.All(addExport.GetParameters(), p => Assert.Equal(typeof(int), p.ParameterType));
        }

        // =================================================================
        // Import path — by-name delegate dict
        // =================================================================

        [Fact]
        public void Load_WithDelegateDict_ImportsDispatch()
        {
            var dllPath = TranspileAndSave(BuildImportModuleWasm(), "Wacs.Loader.Mul");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            // Peek at the import shape first (tool-style interrogation).
            var probe = TranspiledModuleLoader.Load(dllPath,
                imports: new Dictionary<string, Delegate>
                {
                    // Method name on the generated IImports interface uses
                    // InterfaceGenerator.SanitizeName("env_multiply") which
                    // keeps the underscored form intact.
                    ["env_multiply"] = (Func<int, int, int>)((a, b) => a * b),
                });

            Assert.NotNull(probe.ImportsInterface);
            int result = (int)probe.Invoke("call_mul", 6, 7)!;
            Assert.Equal(42, result);
        }

        [Fact]
        public void Load_MissingImport_ThrowsOnInvocation()
        {
            var dllPath = TranspileAndSave(BuildImportModuleWasm(), "Wacs.Loader.Missing");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var loaded = TranspiledModuleLoader.Load(dllPath,
                imports: new Dictionary<string, Delegate>()); // empty — no handlers

            var ex = Assert.Throws<MissingMethodException>(() =>
                loaded.Invoke("call_mul", 1, 2));
            Assert.Contains("env_multiply", ex.Message);
        }

        [Fact]
        public void Load_NoImportsProvided_Throws()
        {
            var dllPath = TranspileAndSave(BuildImportModuleWasm(), "Wacs.Loader.NoArgs");

            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TranspiledModuleLoader.Load(dllPath, imports: null));
            Assert.Contains("requires imports", ex.Message);
        }

        // =================================================================
        // Helpers
        // =================================================================

        private string TranspileAndSave(byte[] wasmBytes, string @namespace)
        {
            var dllPath = Path.Combine(_tempDir, @namespace.Replace('.', '_') + ".dll");

            var runtime = new WasmRuntime();
            // Transpile-time instantiation needs each import bound so the
            // interpreter can link the module. The actual import handlers
            // are irrelevant (we never invoke through the interpreter on
            // this path) — bind stubs. Consumers of the .dll supply the
            // real handlers via TranspiledModuleLoader.Load's imports arg.
            runtime.BindHostFunction<Func<int, int, int>>(
                ("env", "multiply"), (a, b) => 0);

            using var ms = new MemoryStream(wasmBytes);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var transpiler = new ModuleTranspiler(@namespace, new TranspilerOptions());
            var result = transpiler.Transpile(moduleInst, runtime, "WasmModule");
            result.SaveAssembly(dllPath);
            return dllPath;
        }
    }
}
