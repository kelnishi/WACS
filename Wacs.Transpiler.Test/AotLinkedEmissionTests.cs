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
            // BuildMultiMemoryWasm declares two memories — out of
            // IsAotLinkedAutoPromotable's envelope (the AotLinked ctor's
            // memory-allocation IL only handles memory[0]). Auto must fall
            // back to Standard; the saved .dll therefore carries the
            // __WACSInit codec blob.
            var (modInst, runtime) = ParseAndInstantiate(BuildMultiMemoryWasm());
            var opts = new TranspilerOptions { AssemblyName = "Wacs.Auto.Fallback" };
            var result = new ModuleTranspiler("ignored", opts)
                .Transpile(modInst, runtime, "MultiMemMod");

            var dllPath = Path.Combine(Path.GetTempPath(), $"{result.Assembly.GetName().Name}.dll");
            try
            {
                result.SaveAssembly(dllPath);
                var bytes = File.ReadAllBytes(dllPath);
                var asUtf8 = System.Text.Encoding.UTF8.GetString(bytes);
                // Standard emission landed: __WACSInit present.
                Assert.Contains("__WACSInit", asUtf8);
                Assert.Equal(0, (int)Invoke(result, "noop"));
            }
            finally
            {
                if (File.Exists(dllPath)) File.Delete(dllPath);
            }
        }

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
        //   (memory 1) (memory 1)   ;; multi-memory; rejected by Auto
        //   (func (export "noop") (result i32) i32.const 0))
        // Multi-memory is in the reject set of IsAotLinkedAutoPromotable
        // (the AotLinked ctor only emits Newobj for memory[0]), so
        // emission falls back to Standard.
        private static byte[] BuildMultiMemoryWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            0x03, 0x02, 0x01, 0x00,
            // memory section: 2 memories, both min=1 no max
            0x05, 0x05, 0x02, 0x00, 0x01, 0x00, 0x01,
            0x07, 0x08, 0x01, 0x04, 0x6E, 0x6F, 0x6F, 0x70, 0x00, 0x00,
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
