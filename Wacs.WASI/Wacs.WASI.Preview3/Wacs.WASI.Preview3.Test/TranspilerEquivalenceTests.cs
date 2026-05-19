// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.Cli;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Transpiler-parity validation for the Phase 5 host
    /// bindings. The Slice K fixture wasm modules
    /// (`test-now` + `test-random`) round-trip not just
    /// through the interpreter but also through the AOT
    /// transpiler — same wasm bytes, same registered host
    /// functions, IL-emitted call sites, identical return
    /// semantics.
    ///
    /// <para><b>Why this matters.</b> The
    /// <see cref="WasmRuntime.BindHostFunction(string, System.Delegate)"/>
    /// path is shared between engines; the transpiler emits
    /// IL that calls through the runtime's bound delegate
    /// table for each import. If the transpiler is correctly
    /// resolving these imports, the same fixtures that pass
    /// through the interpreter must also pass through the
    /// AOT engine. This test pins that symmetry — per
    /// <c>feedback_symmetric_engines</c>, interpreter and
    /// transpiler share the same public surface and any
    /// host-binding should be observable from either
    /// engine.</para>
    /// </summary>
    public class TranspilerEquivalenceTests
    {
        // ---- wasm bytes (mirror SyntheticFixtureTests) ---------------

        private static void WriteLeb128U32(BinaryWriter w, uint value)
        {
            while (value >= 0x80)
            {
                w.Write((byte)(value | 0x80));
                value >>= 7;
            }
            w.Write((byte)value);
        }

        private static void WriteName(BinaryWriter w, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            WriteLeb128U32(w, (uint)bytes.Length);
            w.Write(bytes);
        }

        private static byte[] BuildImportWrapperFixture(
            string importModule, string importName, string exportName)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D });
            w.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 });

            void WriteSection(byte id, Action<BinaryWriter> body)
            {
                using var bodyMs = new MemoryStream();
                using var bodyW = new BinaryWriter(bodyMs);
                body(bodyW);
                var bodyBytes = bodyMs.ToArray();
                w.Write(id);
                WriteLeb128U32(w, (uint)bodyBytes.Length);
                w.Write(bodyBytes);
            }

            // Type 0: () -> i64
            WriteSection(1, bw =>
            {
                WriteLeb128U32(bw, 1);
                bw.Write((byte)0x60);
                WriteLeb128U32(bw, 0);
                WriteLeb128U32(bw, 1);
                bw.Write((byte)0x7E);
            });
            // Import (module, name) of type 0
            WriteSection(2, bw =>
            {
                WriteLeb128U32(bw, 1);
                WriteName(bw, importModule);
                WriteName(bw, importName);
                bw.Write((byte)0x00);
                WriteLeb128U32(bw, 0);
            });
            // 1 local func of type 0
            WriteSection(3, bw =>
            {
                WriteLeb128U32(bw, 1);
                WriteLeb128U32(bw, 0);
            });
            // Export the local func (idx 1)
            WriteSection(7, bw =>
            {
                WriteLeb128U32(bw, 1);
                WriteName(bw, exportName);
                bw.Write((byte)0x00);
                WriteLeb128U32(bw, 1);
            });
            // Body: call 0; end
            WriteSection(10, bw =>
            {
                WriteLeb128U32(bw, 1);
                using var codeMs = new MemoryStream();
                using var codeW = new BinaryWriter(codeMs);
                WriteLeb128U32(codeW, 0);
                codeW.Write((byte)0x10);
                WriteLeb128U32(codeW, 0);
                codeW.Write((byte)0x0B);
                var codeBytes = codeMs.ToArray();
                WriteLeb128U32(bw, (uint)codeBytes.Length);
                bw.Write(codeBytes);
            });

            return ms.ToArray();
        }

        // ---- Common transpile + invoke pipeline ----------------------

        // Mirrors the private HostedRunner.BuildImportsProxy logic +
        // module activation. Returns the long the export produced.
        private static long TranspileAndInvokeI64Export(
            byte[] wasmBytes, string exportName,
            Action<WasmRuntime> bindHost)
        {
            using var ms = new MemoryStream(wasmBytes);
            var module = BinaryModuleParser.ParseWasm(ms);

            var runtime = new WasmRuntime();
            bindHost(runtime);
            var moduleInst = runtime.InstantiateModule(module);

            var transpiler = new ModuleTranspiler();
            var result = transpiler.Transpile(moduleInst, runtime);

            // Build imports proxy: route each generated IImports
            // method to the runtime's bound host function via
            // CreateStackInvoker.
            object? importsProxy = null;
            if (result.ImportsInterface != null)
            {
                var handlers = new Dictionary<string,
                    Func<object?[], object?>>();
                foreach (var importMethod in result.ImportMethods)
                {
                    Wacs.Core.Module.Import? matched = null;
                    foreach (var imp in moduleInst.Repr.Imports)
                    {
                        if (imp.Desc is not Wacs.Core.Module.ImportDesc.FuncDesc)
                            continue;
                        var expected = InterfaceGenerator.SanitizeName(
                            $"{imp.ModuleName}_{imp.Name}");
                        if (expected != importMethod.Name) continue;
                        matched = imp;
                        break;
                    }
                    if (matched == null)
                    {
                        handlers[importMethod.Name] = _ => null;
                        continue;
                    }

                    var capturedImport = matched;
                    var capturedFuncType = importMethod.WasmType;
                    Wacs.Core.Runtime.Delegates.StackFunc? cached = null;
                    handlers[importMethod.Name] = args =>
                    {
                        if (cached == null)
                        {
                            if (!runtime.TryGetExportedFunction(
                                    (capturedImport.ModuleName, capturedImport.Name),
                                    out var addr))
                                return null;
                            cached = runtime.CreateStackInvoker(addr);
                        }
                        // Frame setup so host bodies that read
                        // ctx.DefaultMemory see this module's memory.
                        var ctx = runtime.ExecContext;
                        var savedFrame = ctx.Frame;
                        var frame = ctx.ReserveFrame(
                            moduleInst, capturedFuncType.ResultType.Arity);
                        ctx.Frame = frame;
                        try
                        {
                            var valueArgs = ToValueArgs(args);
                            var results = cached(valueArgs);
                            if (results.Length == 0) return null;
                            return FromValue(
                                results[0],
                                capturedFuncType.ResultType.Types[0]);
                        }
                        finally
                        {
                            ctx.Frame = savedFrame;
                        }
                    };
                }
                importsProxy = ImportDispatcher.Create(
                    result.ImportsInterface, handlers);
            }

            // Activate the transpiled Module class with the imports
            // proxy and invoke the export.
            var moduleClass = result.ModuleClass
                ?? throw new InvalidOperationException(
                    "Transpiler produced no Module class.");
            var instance = importsProxy != null
                ? Activator.CreateInstance(moduleClass, importsProxy)
                : Activator.CreateInstance(moduleClass);

            // Lookup the export method.
            string? clrName = null;
            foreach (var em in result.ExportMethods)
            {
                if (em.WasmName == exportName) { clrName = em.Name; break; }
            }
            if (clrName == null)
                clrName = InterfaceGenerator.SanitizeName(exportName);

            var method = moduleClass.GetMethod(
                clrName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException(
                    $"Transpiled export '{exportName}' (clr: '{clrName}') " +
                    "not found.");

            object? rv;
            try
            {
                rv = method.Invoke(instance, Array.Empty<object?>());
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo
                    .Throw(tie.InnerException);
                throw; // unreachable
            }
            return (long)rv!;
        }

        private static Value[] ToValueArgs(object?[]? args)
        {
            if (args == null) return Array.Empty<Value>();
            var v = new Value[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                v[i] = args[i] switch
                {
                    Value vv => vv,
                    int iv => new Value(iv),
                    long lv => new Value(lv),
                    float fv => new Value(fv),
                    double dv => new Value(dv),
                    _ => default,
                };
            }
            return v;
        }

        private static object? FromValue(Value val, ValType type) => type switch
        {
            ValType.I32 => val.Data.Int32,
            ValType.I64 => val.Data.Int64,
            ValType.F32 => val.Data.Float32,
            ValType.F64 => val.Data.Float64,
            _ => (object)val,
        };

        // ---- Equivalence tests ---------------------------------------

        [Fact]
        public void Transpiler_test_now_invokes_monotonic_clock_now()
        {
            // Same fixture as SyntheticFixtureTests.Fixture_clock_now
            // but through the transpiler. Confirms the import resolution
            // + delegate-invocation IL path reaches the registered host
            // function and the return value flows back.
            var wasm = BuildImportWrapperFixture(
                "wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15",
                "now",
                "test-now");
            var host = new WasiPreview3Host();

            long n1 = TranspileAndInvokeI64Export(
                wasm, "test-now", host.BindToRuntime);
            // Build a second transpiled module against a fresh
            // runtime so the second value is from a fresh clock —
            // monotonic per-instance only.
            long n2 = TranspileAndInvokeI64Export(
                wasm, "test-now", host.BindToRuntime);

            Assert.True(n1 > 0, $"Transpiled monotonic-clock.now() returned {n1}.");
            // n1 and n2 are from two different host instances but
            // since MonotonicClock is shared across BindToRuntime
            // calls in this test, n2 >= n1 should hold.
            Assert.True(n2 >= n1,
                $"Transpiled monotonic-clock.now() non-decreasing: " +
                $"n1={n1}, n2={n2}.");
        }

        [Fact]
        public void Transpiler_test_random_invokes_get_random_u64()
        {
            var wasm = BuildImportWrapperFixture(
                "wasi:random/random@0.3.0-rc-2026-03-15",
                "get-random-u64",
                "test-random");
            var host = new WasiPreview3Host();

            long a = TranspileAndInvokeI64Export(
                wasm, "test-random", host.BindToRuntime);
            long b = TranspileAndInvokeI64Export(
                wasm, "test-random", host.BindToRuntime);
            long c = TranspileAndInvokeI64Export(
                wasm, "test-random", host.BindToRuntime);

            Assert.True(a != 0 || b != 0 || c != 0,
                "Three consecutive CSPRNG reads through the transpiler " +
                "all returned 0 — either the binding lost the value or " +
                "the BCL's RandomNumberGenerator is misbehaving.");
        }
    }
}
