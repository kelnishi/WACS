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
        public void AutoEmissionPromotesFeasibleModuleToAotLinked()
        {
            // BuildAddWasm is compute-only (no memory, table, global,
            // element, segment, import) → in the safe Auto-promotable
            // subset. Default-constructed TranspilerOptions has
            // Emission = Auto; the saved .dll should have AotLinked's
            // codec-free shape (no __WACSInit, no InitializeFromEmbedded).
            var (modInst, runtime) = ParseAndInstantiate(BuildAddWasm());
            var opts = new TranspilerOptions { AssemblyName = "Wacs.Auto.Promote" };
            Assert.Equal(EmissionTarget.Auto, opts.Emission);

            var result = new ModuleTranspiler("ignored", opts)
                .Transpile(modInst, runtime, "AddMod");

            var dllPath = Path.Combine(Path.GetTempPath(), $"{result.Assembly.GetName().Name}.dll");
            try
            {
                result.SaveAssembly(dllPath);
                var bytes = File.ReadAllBytes(dllPath);
                var asUtf8 = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.DoesNotContain("__WACSInit", asUtf8);
                Assert.DoesNotContain("InitializeFromEmbedded", asUtf8);
                Assert.Equal(42, (int)Invoke(result, "add", 7, 35));
            }
            finally
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
            }
        }

        [Fact]
        public void AutoEmissionFallsBackToStandardForUnsupportedShape()
        {
            // BuildImportMemoryWasm imports a memory — out of
            // IsAotLinkedAutoPromotable's envelope (the AotLinked ctor
            // allocates fresh memory slots and has no linker hook for
            // imported instances). Auto must fall back to Standard;
            // the saved .dll therefore carries the __WACSInit codec
            // blob.
            using var fs = new System.IO.MemoryStream(BuildImportMemoryWasm());
            var module = BinaryModuleParser.ParseWasm(fs);
            var runtime = new WasmRuntime();
            // The host-side memory needs to be discoverable by name;
            // the simplest path is to instantiate a tiny "env" module
            // that exports a memory and let the linker resolve.
            var envBytes = new byte[]
            {
                0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
                // memory: 1 memory, min=1 no max
                0x05, 0x03, 0x01, 0x00, 0x01,
                // export: "mem" -> memory 0
                0x07, 0x07, 0x01, 0x03, 0x6D, 0x65, 0x6D, 0x02, 0x00,
            };
            using var envFs = new System.IO.MemoryStream(envBytes);
            var envModule = BinaryModuleParser.ParseWasm(envFs);
            runtime.RegisterModule("env", runtime.InstantiateModule(envModule));
            var modInst = runtime.InstantiateModule(module);

            var opts = new TranspilerOptions { AssemblyName = "Wacs.Auto.Fallback" };
            var result = new ModuleTranspiler("ignored", opts)
                .Transpile(modInst, runtime, "ImportMemMod");

            var dllPath = Path.Combine(Path.GetTempPath(), $"{result.Assembly.GetName().Name}.dll");
            try
            {
                result.SaveAssembly(dllPath);
                var bytes = File.ReadAllBytes(dllPath);
                var asUtf8 = System.Text.Encoding.UTF8.GetString(bytes);
                // Standard emission landed: __WACSInit codec blob is
                // present (AotLinked emission omits this type entirely
                // — see AotLinkedSavedDllOmitsCodecHolderType).
                Assert.Contains("__WACSInit", asUtf8);
            }
            finally
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
            }
        }

        [Fact]
        public void ImportDispatcherThrowsOnMissingHandlerByDefault()
        {
            // (module
            //   (import "env" "ext" (func (result i32)))
            //   (func (export "noop") (result i32) call 0))
            // Build the proxy with an EMPTY handler dict, instantiate
            // the Module, then call noop — the import is invoked, the
            // dispatcher has no handler for it, and the default loud
            // behavior should throw an InvalidOperationException
            // naming the missing import.
            using var fs = new System.IO.MemoryStream(BuildImportFuncWasm());
            var module = BinaryModuleParser.ParseWasm(fs);
            var runtime = new WasmRuntime();
            // Bind a host fn so InstantiateModule resolves the import,
            // even though we'll *replace* the dispatcher at the
            // Activator.CreateInstance step with an empty-handler one.
            runtime.BindHostFunction<System.Func<int>>(("env", "ext"), () => 0);
            var modInst = runtime.InstantiateModule(module);
            var result = new ModuleTranspiler("Wacs.AotLink.LoudMissing",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "LoudMod");

            var importsProxy = Wacs.Transpiler.Cli.ImportDispatcher.Create(
                result.ImportsInterface!,
                new System.Collections.Generic.Dictionary<
                    string, System.Func<object?[], object?>>());
            var instance = Activator.CreateInstance(result.ModuleClass!, importsProxy)!;
            var noopMethod = result.ModuleClass!.GetMethod("noop",
                BindingFlags.Public | BindingFlags.Instance)!;

            var tie = Assert.Throws<TargetInvocationException>(() =>
                noopMethod.Invoke(instance, Array.Empty<object>()));
            var inner = Assert.IsType<InvalidOperationException>(tie.InnerException);
            Assert.Contains("env_ext", inner.Message);
            Assert.Contains("No handler registered", inner.Message);
        }

        [Fact]
        public void ImportDispatcherLenientFallsBackToDefault()
        {
            // Same fixture, but with lenient: true — missing handler
            // should return default(int) = 0 instead of throwing. The
            // export's `call 0` returns whatever the import returned, so
            // we get 0.
            using var fs = new System.IO.MemoryStream(BuildImportFuncWasm());
            var module = BinaryModuleParser.ParseWasm(fs);
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<System.Func<int>>(("env", "ext"), () => 0);
            var modInst = runtime.InstantiateModule(module);
            var result = new ModuleTranspiler("Wacs.AotLink.Lenient",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "LenientMod");

            var importsProxy = Wacs.Transpiler.Cli.ImportDispatcher.Create(
                result.ImportsInterface!,
                new System.Collections.Generic.Dictionary<
                    string, System.Func<object?[], object?>>(),
                lenient: true);
            var instance = Activator.CreateInstance(result.ModuleClass!, importsProxy)!;
            var noopMethod = result.ModuleClass!.GetMethod("noop",
                BindingFlags.Public | BindingFlags.Instance)!;

            int n = (int)noopMethod.Invoke(instance, Array.Empty<object>())!;
            Assert.Equal(0, n);
        }

        [Fact]
        public void AotLinkedSupportsImportedFunction()
        {
            // (module
            //   (import "env" "ext" (func (result i32)))
            //   (func (export "noop") (result i32) call 0))
            //
            // Imported function — wired through IImports / the
            // ImportDelegates[] path that Standard and AotLinked share.
            // Auto must promote (function imports are now in the safe
            // subset). The Module class ctor takes an IImports arg
            // and the export's `call 0` dispatches into the bound host
            // delegate.
            using var fs = new System.IO.MemoryStream(BuildImportFuncWasm());
            var module = BinaryModuleParser.ParseWasm(fs);
            var runtime = new WasmRuntime();
            runtime.BindHostFunction<System.Func<int>>(("env", "ext"), () => 7);
            var modInst = runtime.InstantiateModule(module);

            var aotResult = new ModuleTranspiler("Wacs.AotLink.ImportFn",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "ImportFnMod");

            // Trim-evidence: AotLinked saved .dll has no codec blob
            // even with imports.
            var dllPath = Path.Combine(Path.GetTempPath(),
                $"{aotResult.Assembly.GetName().Name}.dll");
            try
            {
                aotResult.SaveAssembly(dllPath);
                var bytes = File.ReadAllBytes(dllPath);
                var asUtf8 = System.Text.Encoding.UTF8.GetString(bytes);
                Assert.DoesNotContain("__WACSInit", asUtf8);
            }
            finally
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
            }

            // Ctor takes IImports — build a DispatchProxy adapter that
            // forwards method calls into a host-supplied delegate map.
            var importsInterface = aotResult.ImportsInterface!;
            var handlers = new System.Collections.Generic.Dictionary<
                string, System.Func<object?[], object?>>
            {
                ["env_ext"] = _ => 7,
            };
            var importsProxy = Wacs.Transpiler.Cli.ImportDispatcher.Create(
                importsInterface, handlers);

            var instance = Activator.CreateInstance(aotResult.ModuleClass!, importsProxy)!;
            var noopMethod = aotResult.ModuleClass!.GetMethod("noop",
                BindingFlags.Public | BindingFlags.Instance)!;
            int result = (int)noopMethod.Invoke(instance, Array.Empty<object>())!;
            Assert.Equal(7, result);
        }

        [Fact]
        public void AotLinkedSupportsLocalExceptionTag()
        {
            // (module
            //   (tag $t)              ;; one local tag, no params, no result
            //   (func (export "noop") (result i32) i32.const 42))
            //
            // The tag's mere existence puts the module on the path that
            // EmitTagsArray now handles. Verifies allocation + the export
            // runs to completion. Throw/catch semantics test elsewhere.
            var (m1, r1) = ParseAndInstantiate(BuildLocalTagWasm());
            var stdResult = new ModuleTranspiler("Wacs.AotLink.Tag.Std",
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(m1, r1, "TagMod");
            Assert.Equal(42, (int)Invoke(stdResult, "noop"));
            AssertTagSlotsAllocated(stdResult, expected: 1);

            var (m2, r2) = ParseAndInstantiate(BuildLocalTagWasm());
            var aotResult = new ModuleTranspiler("Wacs.AotLink.Tag",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(m2, r2, "TagMod");
            Assert.Equal(42, (int)Invoke(aotResult, "noop"));
            AssertTagSlotsAllocated(aotResult, expected: 1);
        }

        private static void AssertTagSlotsAllocated(TranspilationResult result, int expected)
        {
            var instance = Activator.CreateInstance(result.ModuleClass!)
                ?? throw new Exception("Activator returned null");
            var ctxField = result.ModuleClass!.GetField("_ctx",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Exception("_ctx field missing");
            var ctx = (Wacs.Transpiler.AOT.ThinContext)ctxField.GetValue(instance)!;
            Assert.NotNull(ctx.Tags);
            Assert.Equal(expected, ctx.Tags.Length);
            for (int i = 0; i < ctx.Tags.Length; i++)
                Assert.NotNull(ctx.Tags[i]);
        }

        // (module
        //   (type $void (func))
        //   (type $ret_i32 (func (result i32)))
        //   (func (export "noop") (result i32) i32.const 42)
        //   (tag $t (type $void)))
        // Tag section (id 13): 1 entry, attribute=0, typeidx of tag's
        // function-type signature.
        private static byte[] BuildLocalTagWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: 2 types
            //   type 0: () -> i32  (4 bytes: 0x60 0x00 0x01 0x7F)
            //   type 1: () -> ()   (3 bytes: 0x60 0x00 0x00)
            0x01, 0x08, 0x02, 0x60, 0x00, 0x01, 0x7F, 0x60, 0x00, 0x00,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // tag: id=13, 1 entry: attribute=0, typeidx=1
            0x0D, 0x03, 0x01, 0x00, 0x01,
            // export: "noop" -> func 0
            0x07, 0x08, 0x01, 0x04, 0x6E, 0x6F, 0x6F, 0x70, 0x00, 0x00,
            // code: 1 body, locals=0, i32.const 42, end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x41, 0x2A, 0x0B,
        };

        [Fact]
        public void AotLinkedSupportsMultiMemory()
        {
            // (module
            //   (memory 1) (memory 2)
            //   (func (export "size0") (result i32) memory.size 0)
            //   (func (export "size1") (result i32) memory.size 1))
            //
            // Two memories at distinct sizes (1 page and 2 pages) +
            // exports that ask each its size. Proves AotLinked's
            // EmitMemoryArray correctly allocates both memories and
            // memory.size with non-zero memidx threads through. The
            // sibling AotLinkedSupportsActiveDataWithExplicitMemIdx
            // test covers the active-data-with-explicit-memidx path.
            var (m1, r1) = ParseAndInstantiate(BuildMultiMemoryWasm());
            var stdResult = new ModuleTranspiler("Wacs.AotLink.MultiMem.Std",
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(m1, r1, "MultiMemMod");
            Assert.Equal(1, (int)Invoke(stdResult, "size0"));
            Assert.Equal(2, (int)Invoke(stdResult, "size1"));

            var (m2, r2) = ParseAndInstantiate(BuildMultiMemoryWasm());
            var aotResult = new ModuleTranspiler("Wacs.AotLink.MultiMem",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(m2, r2, "MultiMemMod");
            Assert.Equal(1, (int)Invoke(aotResult, "size0"));
            Assert.Equal(2, (int)Invoke(aotResult, "size1"));
        }

        // (module
        //   (memory 1) (memory 2)
        //   (func (export "size0") (result i32) memory.size 0)
        //   (func (export "size1") (result i32) memory.size 1))
        //
        // memory.size opcode 0x3F takes a memidx leb128 (replaces the
        // pre-multi-memory reserved-byte slot).
        private static byte[] BuildMultiMemoryWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 2 funcs, type 0
            0x03, 0x03, 0x02, 0x00, 0x00,
            // memory: 2 memories — min=1 and min=2 (no max on either)
            0x05, 0x05, 0x02, 0x00, 0x01, 0x00, 0x02,
            // export: 2 exports
            0x07, 0x11, 0x02,
                0x05, 0x73, 0x69, 0x7A, 0x65, 0x30, 0x00, 0x00,
                0x05, 0x73, 0x69, 0x7A, 0x65, 0x31, 0x00, 0x01,
            // code: 2 bodies.
            // Body 0: locals=0 + memory.size 0 (0x3F 0x00) + end = 4 bytes
            // Body 1: locals=0 + memory.size 1 (0x3F 0x01) + end = 4 bytes
            // Section: count(1) + size(1) + body0(4) + size(1) + body1(4) = 11 = 0x0B
            0x0A, 0x0B, 0x02,
                0x04, 0x00, 0x3F, 0x00, 0x0B,
                0x04, 0x00, 0x3F, 0x01, 0x0B,
        };

        [Fact]
        public void AotLinkedSupportsActiveDataWithExplicitMemIdx()
        {
            // (module
            //   (memory $m0 1) (memory $m1 1)
            //   (data (memory $m0) (i32.const 0) "AAAA")
            //   (data (memory $m1) (i32.const 0) "BBBB")
            //   (func (export "load_m0") (result i32) i32.const 0 i32.load $m0)
            //   (func (export "load_m1") (result i32) i32.const 0 i32.load $m1))
            //
            // Both Standard and AotLinked emission must initialize memory
            // 1 from a data segment with the ActiveExplicit (DataFlags=2)
            // encoding (memidx LEB128 between the flag byte and the
            // offset expression). Loads also carry the multi-memory
            // memarg encoding (align bit 6 set + memidx LEB128).
            // Each Invoke creates a fresh Module instance, exercising the
            // re-instantiation path: active data segments are dropped
            // from the global ModuleInit registry after instance 1's
            // ctor (per spec §4.5.4), so instance 2's ctor must restore
            // from data.SavedDataSegments (see InitializationHelper
            // step 4a) to re-init memory correctly.
            var (m1, r1) = ParseAndInstantiate(BuildActiveDataExplicitMemIdxWasm());
            var stdResult = new ModuleTranspiler("Wacs.AotLink.MultiMem.Data.Std",
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(m1, r1, "DataMemMod");
            Assert.Equal(0x41414141, (int)Invoke(stdResult, "load_m0"));
            Assert.Equal(0x42424242, (int)Invoke(stdResult, "load_m1"));

            var (m2, r2) = ParseAndInstantiate(BuildActiveDataExplicitMemIdxWasm());
            var aotResult = new ModuleTranspiler("Wacs.AotLink.MultiMem.Data",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(m2, r2, "DataMemMod");
            Assert.Equal(0x41414141, (int)Invoke(aotResult, "load_m0"));
            Assert.Equal(0x42424242, (int)Invoke(aotResult, "load_m1"));
        }

        // Bytes produced by `wat2wasm --enable-multi-memory` against:
        //   (module
        //     (memory $m0 1) (memory $m1 1)
        //     (data (memory $m0) (i32.const 0) "AAAA")
        //     (data (memory $m1) (i32.const 0) "BBBB")
        //     (func (export "load_m0") (result i32) i32.const 0 i32.load $m0)
        //     (func (export "load_m1") (result i32) i32.const 0 i32.load $m1))
        //
        // Notable encodings: data segment 1 uses DataFlags=2
        // (ActiveExplicit) with a memidx LEB128, and i32.load $m1 emits
        // align byte 0x42 (bit 6 = "memidx follows" + log2 align 2)
        // followed by the memidx LEB128 then the offset.
        private static byte[] BuildActiveDataExplicitMemIdxWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 2 funcs, type 0
            0x03, 0x03, 0x02, 0x00, 0x00,
            // memory: 2 memories, min=1 each
            0x05, 0x05, 0x02, 0x00, 0x01, 0x00, 0x01,
            // export: load_m0 → func 0, load_m1 → func 1
            0x07, 0x15, 0x02,
                0x07, 0x6C, 0x6F, 0x61, 0x64, 0x5F, 0x6D, 0x30, 0x00, 0x00,
                0x07, 0x6C, 0x6F, 0x61, 0x64, 0x5F, 0x6D, 0x31, 0x00, 0x01,
            // code: 2 bodies.
            // Body 0: locals=0 + i32.const 0 + i32.load (align=2, offset=0) + end (7 bytes)
            // Body 1: locals=0 + i32.const 0 + i32.load (align=0x42, memidx=1, offset=0) + end (8 bytes)
            0x0A, 0x12, 0x02,
                0x07, 0x00, 0x41, 0x00, 0x28, 0x02, 0x00, 0x0B,
                0x08, 0x00, 0x41, 0x00, 0x28, 0x42, 0x01, 0x00, 0x0B,
            // data: 2 segments.
            // Segment 0: flag=0 (ActiveDefault) + i32.const 0 + end + 4 bytes "AAAA"
            // Segment 1: flag=2 (ActiveExplicit) + memidx=1 + i32.const 0 + end + 4 bytes "BBBB"
            0x0B, 0x14, 0x02,
                0x00, 0x41, 0x00, 0x0B, 0x04, 0x41, 0x41, 0x41, 0x41,
                0x02, 0x01, 0x41, 0x00, 0x0B, 0x04, 0x42, 0x42, 0x42, 0x42,
        };

        [Fact]
        public void AotLinkedSupportsPassiveElementSegmentRoundTrip()
        {
            // (module
            //   (type $ft (func (result i32)))
            //   (func $forty_two (result i32) i32.const 42)
            //   (table 1 funcref)
            //   (elem funcref (ref.func $forty_two))     ;; passive segment
            //   (func (export "init_then_call") (result i32)
            //     i32.const 0     ;; dst slot
            //     i32.const 0     ;; src offset in segment
            //     i32.const 1     ;; len
            //     table.init 0    ;; copy passive segment 0 into table
            //     i32.const 0
            //     call_indirect (type $ft)))
            //
            // Exercises the passive-element registration path:
            // EmitPassiveElementSegmentRegistrations stamps the segment
            // values into ModuleInit at ctor time, then table.init
            // resolves the segment by id, copies into table[0]; the
            // following call_indirect dispatches into $forty_two.
            var (modInst, runtime) = ParseAndInstantiate(BuildPassiveTableInitWasm());
            var result = new ModuleTranspiler("Wacs.AotLink.PassiveElem",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "PassiveElemMod");
            int n = (int)Invoke(result, "init_then_call");
            Assert.Equal(42, n);
        }

        // Same shape as documented in
        // AotLinkedSupportsPassiveElementSegmentRoundTrip. Element
        // segment header byte 0x05 = passive, type funcref, vec of
        // initializer expressions.
        private static byte[] BuildPassiveTableInitWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 2 funcs, both type 0
            0x03, 0x03, 0x02, 0x00, 0x00,
            // table: 1 table, funcref limits min=1 (no max)
            0x04, 0x04, 0x01, 0x70, 0x00, 0x01,
            // export: "init_then_call" (14 chars) -> func 1
            0x07, 0x12, 0x01, 0x0E,
                0x69, 0x6E, 0x69, 0x74, 0x5F, 0x74, 0x68, 0x65, 0x6E,
                0x5F, 0x63, 0x61, 0x6C, 0x6C,
                0x00, 0x01,
            // element: 1 segment, mode 0x05 (passive, vec of exprs),
            //          elemtype = funcref (0x70), 1 init expr
            //          (ref.func 0; end).
            // Layout: count(1) + mode(1) + elemtype(1) + n(1)
            //         + expr(0xD2 0x00 0x0B = 3 bytes) = 7 bytes
            0x09, 0x07, 0x01, 0x05, 0x70, 0x01, 0xD2, 0x00, 0x0B,
            // code: 2 bodies.
            // Body 0: $forty_two — locals=0 + i32.const 42 + end = 4 bytes.
            // Body 1 contents (after size prefix): locals=0 (1)
            //   + i32.const 0 (2) + i32.const 0 (2) + i32.const 1 (2)
            //   + table.init 0 0 (4) + i32.const 0 (2)
            //   + call_indirect 0 0 (3) + end (1) = 17 bytes (0x11).
            // Section: count(1) + body0_size(1) + body0(4)
            //        + body1_size(1) + body1(17) = 24 bytes (0x18).
            0x0A, 0x18, 0x02,
                0x04, 0x00, 0x41, 0x2A, 0x0B,
                0x11, 0x00,
                    0x41, 0x00, 0x41, 0x00, 0x41, 0x01,
                    0xFC, 0x0C, 0x00, 0x00,
                    0x41, 0x00, 0x11, 0x00, 0x00,
                    0x0B,
        };

        [Fact]
        public void AotLinkedSupportsPassiveDataSegmentRoundTrip()
        {
            // (module
            //   (memory 1)
            //   (data "\2A")    ;; passive
            //   (func (export "load_passive") (result i32)
            //     i32.const 0    ;; dst
            //     i32.const 0    ;; src offset in segment
            //     i32.const 1    ;; len
            //     memory.init 0  ;; copy passive segment 0 into memory
            //     i32.const 0
            //     i32.load8_u))
            //
            // Exercises the passive-data registration path:
            // EmitPassiveDataSegmentRegistrations stamps the bytes into
            // ModuleInit at ctor time, then memory.init resolves the
            // segment by id and copies the byte 0x2A to memory[0]; load8_u
            // reads it back.
            var (modInst, runtime) = ParseAndInstantiate(BuildPassiveMemoryInitWasm());
            var result = new ModuleTranspiler("Wacs.AotLink.Passive",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "PassiveMod");
            int read = (int)Invoke(result, "load_passive");
            Assert.Equal(0x2A, read);
        }

        // Same shape as documented in AotLinkedSupportsPassiveDataSegmentRoundTrip.
        // Includes a DataCount section (id 12) before code — required for
        // any module declaring passive data segments per WASM 3.0 §5.5.18.
        private static byte[] BuildPassiveMemoryInitWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // memory: 1 memory, min=1
            0x05, 0x03, 0x01, 0x00, 0x01,
            // export: "load_passive" -> func 0
            0x07, 0x10, 0x01, 0x0C,
                0x6C, 0x6F, 0x61, 0x64, 0x5F, 0x70, 0x61, 0x73, 0x73, 0x69, 0x76, 0x65,
                0x00, 0x00,
            // data count: 1 (required when passive data segments present)
            0x0C, 0x01, 0x01,
            // code: i32.const 0; i32.const 0; i32.const 1;
            //       memory.init 0 (0xFC 0x08 segidx=0 memidx=0);
            //       i32.const 0; i32.load8_u align=0 offset=0; end
            // body bytes: locals=0 + i32.const 0 + i32.const 0 + i32.const 1
            //   + 0xFC 0x08 0x00 0x00 + i32.const 0 + i32.load8_u 0x00 0x00 + end
            // = 1 + 2 + 2 + 2 + 4 + 2 + 3 + 1 = 17 bytes
            0x0A, 0x13, 0x01, 0x11,
                0x00,
                0x41, 0x00,
                0x41, 0x00,
                0x41, 0x01,
                0xFC, 0x08, 0x00, 0x00,
                0x41, 0x00,
                0x2D, 0x00, 0x00,
                0x0B,
            // data section: 1 segment, mode 0x01 (passive), 1 byte 0x2A
            0x0B, 0x04, 0x01, 0x01, 0x01, 0x2A,
        };

        // (module
        //   (import "env" "ext" (func (result i32)))
        //   (func (export "noop") (result i32) call 0))
        // Imported function — Auto allows this (function imports go
        // through the IImports / ImportDelegates path used by both
        // emission targets), so this fixture is in-envelope.
        private static byte[] BuildImportFuncWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // import: 1 entry — "env"."ext" func type 0
            0x02, 0x0B, 0x01,
                0x03, 0x65, 0x6E, 0x76,
                0x03, 0x65, 0x78, 0x74,
                0x00, 0x00,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // export: "noop" -> func 1 (the import is func 0)
            0x07, 0x08, 0x01, 0x04, 0x6E, 0x6F, 0x6F, 0x70, 0x00, 0x01,
            // code: 1 body, size=4, locals=0, call 0 (the import), end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x10, 0x00, 0x0B,
        };

        // (module
        //   (import "env" "mem" (memory 1))
        //   (func (export "noop") (result i32) i32.const 0))
        // Imported memory rejects via IsAotLinkedAutoPromotable's
        // ImportDesc.MemDesc check — AotLinked's standalone ctor
        // allocates fresh memory slots and has no linker hook to
        // overwrite slot 0 with the imported MemoryInstance.
        private static byte[] BuildImportMemoryWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // import: 1 entry — "env"."mem" memory limits min=1 no max
            // entry: name_len(1)+"env"(3)+name_len(1)+"mem"(3)+kind(1)+limits(2) = 11
            // section: count(1) + entry(11) = 12 = 0x0C
            0x02, 0x0C, 0x01,
                0x03, 0x65, 0x6E, 0x76,
                0x03, 0x6D, 0x65, 0x6D,
                0x02, 0x00, 0x01,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // export: "noop" -> func 0
            0x07, 0x08, 0x01, 0x04, 0x6E, 0x6F, 0x6F, 0x70, 0x00, 0x00,
            // code: 1 body, locals=0, i32.const 0, end
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x41, 0x00, 0x0B,
        };

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
        public void AotLinkedSupportsGlobalWithI32Const()
        {
            // First confirm the wasm itself parses + reads 99 via Standard
            // emission — pin down whether this is a wasm-bytes problem or
            // an AotLinked emission problem.
            var (m1, r1) = ParseAndInstantiate(BuildGlobalWasm());
            var stdResult = new ModuleTranspiler("Wacs.AotLink.GlobStd",
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(m1, r1, "GlobMod");
            int stdRead = (int)Invoke(stdResult, "read");
            Assert.Equal(99, stdRead);

            // Now AotLinked.
            var (m2, r2) = ParseAndInstantiate(BuildGlobalWasm());
            var aotResult = new ModuleTranspiler("Wacs.AotLink.Glob",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(m2, r2, "GlobMod");
            int aotRead = (int)Invoke(aotResult, "read");
            Assert.Equal(99, aotRead);
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

        [Fact]
        public void AotLinkedSupportsCallIndirectViaElementSegment()
        {
            // (module
            //   (type $ft (func (result i32)))
            //   (table 1 funcref)
            //   (elem (i32.const 0) $forty_two)
            //   (func $forty_two (result i32) i32.const 42)
            //   (func (export "trampoline") (result i32)
            //     i32.const 0 call_indirect (type $ft)))
            // call_indirect 0 hits the table[0] slot, which the element
            // segment populates with funcidx for $forty_two.
            var (modInst, runtime) = ParseAndInstantiate(BuildCallIndirectWasm());
            var result = new ModuleTranspiler("Wacs.AotLink.CallInd",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(modInst, runtime, "CallIndMod");
            int n = (int)Invoke(result, "trampoline");
            Assert.Equal(42, n);
        }

        private static byte[] BuildCallIndirectWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32 (one type)
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 2 funcs, both type 0
            0x03, 0x03, 0x02, 0x00, 0x00,
            // table: 1 table, funcref limits min=1 (no max)
            0x04, 0x04, 0x01, 0x70, 0x00, 0x01,
            // export: "trampoline" -> func 1
            0x07, 0x0E, 0x01, 0x0A, 0x74, 0x72, 0x61, 0x6D, 0x70, 0x6F, 0x6C, 0x69, 0x6E, 0x65, 0x00, 0x01,
            // element: 1 segment, mode 0, (i32.const 0), 1 funcidx 0
            0x09, 0x07, 0x01, 0x00, 0x41, 0x00, 0x0B, 0x01, 0x00,
            // code: 2 bodies — func 0: i32.const 42; end. func 1: i32.const 0; call_indirect type 0 table 0; end.
            // Body 1 size = 1 (locals=0 byte) + 3 (i32.const 0x2A, end) = 4.
            // Body 2 size = 1 + 5 (i32.const 0, call_indirect type 0 table 0, end) = 6.
            // Wait: i32.const 0 = 0x41 0x00 = 2 bytes; call_indirect = 0x11 0x00 0x00 = 3; end = 0x0B = 1 → 6 bytes content.
            // Section bytes: count(1) + body1.size(1) + body1(4) + body2.size(1) + body2(7) = 14 = 0x0E
            0x0A, 0x0E, 0x02,
              0x04, 0x00, 0x41, 0x2A, 0x0B,
              0x07, 0x00, 0x41, 0x00, 0x11, 0x00, 0x00, 0x0B,
        };

        [Fact]
        public void AotLinkedSupportsNonNullFuncrefGlobal()
        {
            // (module
            //   (func $f (result i32) i32.const 42)
            //   (global $g funcref (ref.func $f))
            //   (export "f" (func 0)))
            //
            // The global's init `(ref.func 0)` lands as Value{Type=FuncRef,
            // Data.Ptr = 0}; pre-fix AssertAotLinkedFeasible rejected it
            // because IsNullRef = false. After: EmitPrimitiveValue routes
            // non-null FuncRef/ExternRef through the (ValType, int) ctor.
            // Verifies: (a) Standard and AotLinked both transpile; (b) the
            // emitted Module's _ctx.Globals[0] carries the matching
            // (FuncRef, 0) Value under both paths.
            var (m1, r1) = ParseAndInstantiate(BuildFuncRefGlobalWasm());
            var stdResult = new ModuleTranspiler("Wacs.AotLink.FRefStd",
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(m1, r1, "FRefMod");
            AssertFuncRefGlobal(stdResult, expectedFuncIdx: 0);

            var (m2, r2) = ParseAndInstantiate(BuildFuncRefGlobalWasm());
            var aotResult = new ModuleTranspiler("Wacs.AotLink.FRefAot",
                new TranspilerOptions { Emission = EmissionTarget.AotLinked })
                .Transpile(m2, r2, "FRefMod");
            AssertFuncRefGlobal(aotResult, expectedFuncIdx: 0);
        }

        private static void AssertFuncRefGlobal(TranspilationResult result, int expectedFuncIdx)
        {
            var instance = Activator.CreateInstance(result.ModuleClass!)
                ?? throw new Exception("Activator returned null");
            var ctxField = result.ModuleClass!.GetField("_ctx",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Exception("_ctx field missing");
            var ctx = (Wacs.Transpiler.AOT.ThinContext)ctxField.GetValue(instance)!;
            Assert.NotNull(ctx.Globals);
            Assert.True(ctx.Globals.Length >= 1, "Module ctx should expose at least 1 global.");
            var g0 = ctx.Globals[0].Value;
            Assert.Equal(Wacs.Core.Types.Defs.ValType.FuncRef, g0.Type);
            Assert.False(g0.IsNullRef,
                $"Global 0 should be a non-null funcref; saw IsNullRef=true.");
            Assert.Equal((long)expectedFuncIdx, g0.Data.Ptr);
        }

        // (module
        //   (func $f (result i32) i32.const 42)
        //   (global $g funcref (ref.func $f))
        //   (export "f" (func 0)))
        // ref.func encoding: 0xD2 <leb128 funcidx>.
        private static byte[] BuildFuncRefGlobalWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // global: 1 global, valtype=funcref(0x70), mut=const(0x00),
            //         init = ref.func 0 (0xD2 0x00); end (0x0B)
            0x06, 0x06, 0x01, 0x70, 0x00, 0xD2, 0x00, 0x0B,
            // export: "f" -> func 0
            0x07, 0x05, 0x01, 0x01, 0x66, 0x00, 0x00,
            // code: 1 body, size=4, locals=0, i32.const 42 (0x41 0x2A), end (0x0B)
            0x0A, 0x06, 0x01, 0x04, 0x00, 0x41, 0x2A, 0x0B,
        };

        // (module
        //   (global $g i32 (i32.const 99))
        //   (func (export "read") (result i32) global.get $g))
        // Note: 99 sleb128 = 0xE3 0x00 (single-byte 0x63 would be -29, since
        // sleb128 sign-extends bit 6 of the last byte).
        private static byte[] BuildGlobalWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            // type: () -> i32
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            // function: 1 func, type 0
            0x03, 0x02, 0x01, 0x00,
            // global: 1 global, i32 const 99 (encoded 0xE3 0x00)
            0x06, 0x07, 0x01, 0x7F, 0x00, 0x41, 0xE3, 0x00, 0x0B,
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
