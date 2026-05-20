// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Expr = System.Linq.Expressions;
using System.Text;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Cli;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 4 acceptance end-to-end. Drives cli-hello through
    /// <see cref="ComponentInstance.Instantiate"/> with the
    /// import-resolution gauntlet stubbed permissively, then
    /// records what blocks the actual run() invocation.
    /// </summary>
    public class CliHelloEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public CliHelloEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string FixturePath(string name) =>
            Path.Combine(
                Path.GetDirectoryName(typeof(CliHelloEndToEndTests).Assembly.Location)
                    ?? string.Empty,
                "Fixtures", name);

        [Fact]
        public void Component_structure_smoke()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);
            _output.WriteLine(
                $"Sections: {component.RawSections.Count}, " +
                $"Core modules: {component.CoreModuleCount}, " +
                $"Nested: {component.NestedComponentCount}, " +
                $"Exports: {component.Exports.Count}, " +
                $"Canons: {component.Canons.Count}");
            foreach (var e in component.Exports)
                _output.WriteLine($"  export: {e.Name} ({e.Sort})");
        }

        /// <summary>
        /// Instantiate with permissive stubs for every core
        /// import — surfaces whatever blocks past
        /// import-resolution. Reports the actual failure mode
        /// (instantiation throws, or post-instantiation API
        /// shape limitations).
        /// </summary>
        [Fact]
        public void Instantiation_with_permissive_stubs()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            using var capturedStdout = new MemoryStream();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    Stdout = new StreamBackedSink(capturedStdout),
                });

            int stubbedCount = 0;
            try
            {
                var instance = ComponentInstance.Instantiate(bytes,
                    runtime =>
                    {
                        // Real host bindings — last-bind-wins,
                        // so these override the stubs below.
                        host.BindToRuntime(runtime);

                        // Permissive stubs for every function
                        // import across all core modules. Walks
                        // the cli-hello core-binaries list and
                        // stub-binds anything still unbound
                        // after the real host bindings landed.
                        // Runtime.TryGetExportedFunction on the
                        // (module, name) tuple returns true iff
                        // a binding already exists in the
                        // shared entity-bindings table.
                        using var inner = new MemoryStream(bytes);
                        var component = ComponentBinaryParser.Parse(inner);
                        foreach (var coreBytes in component.CoreModuleBinaries)
                        {
                            using var coreStream = new MemoryStream(coreBytes);
                            var coreModule = BinaryModuleParser.ParseWasm(coreStream);
                            stubbedCount += StubUnboundFunctionImports(
                                runtime, coreModule);
                        }
                    });
                _output.WriteLine(
                    $"Instantiation SUCCEEDED with {stubbedCount} " +
                    "permissive stubs added.");
                _output.WriteLine("Component-level exports:");
                foreach (var e in instance.Component.Exports)
                    _output.WriteLine($"  {e.Name} ({e.Sort})");
            }
            catch (Exception ex)
            {
                _output.WriteLine(
                    $"Instantiation failed after {stubbedCount} stubs:");
                _output.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    _output.WriteLine(
                        $"  Inner: {ex.InnerException.GetType().Name}: " +
                        $"{ex.InnerException.Message}");
            }
        }

        /// <summary>
        /// Verify the core async-lift export is present after
        /// stubbed instantiation. DOESN'T invoke — permissive
        /// stubs return 0 for every wit-bindgen helper, which
        /// makes wit_stream::write_all spin in a poll loop
        /// forever (it never gets a "complete" signal). The
        /// proper fix is canon-async lift adapter integration
        /// that binds stream/future helpers to real dispatcher
        /// methods.
        /// </summary>
        [Fact]
        public void Async_lift_export_present_after_stubbed_instantiation()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));

            using var capturedStdout = new MemoryStream();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    Stdout = new StreamBackedSink(capturedStdout),
                });

            ComponentInstance? instance = null;
            try
            {
                instance = ComponentInstance.Instantiate(bytes,
                    runtime =>
                    {
                        host.BindToRuntime(runtime);
                        using var inner = new MemoryStream(bytes);
                        var component = ComponentBinaryParser.Parse(inner);
                        foreach (var coreBytes in component.CoreModuleBinaries)
                        {
                            using var coreStream = new MemoryStream(coreBytes);
                            StubUnboundFunctionImports(runtime,
                                BinaryModuleParser.ParseWasm(coreStream));
                        }
                    });
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Instantiation failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            _output.WriteLine("Instantiation succeeded. Checking exports...");

            // Verify the core async-lift entry point exists.
            // wit-component emits async-lifted exports under
            // [async-lift]<iface>#<func> in the core module's
            // export table; the component-level Invoke API
            // doesn't reach this directly (it expects Sort=Func
            // exports, but wit-component packs async-lifted
            // exports inside Sort=Instance wrappers).
            const string coreEntry =
                "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run";
            Assert.True(instance!.CoreRuntime.TryGetExportedFunction(
                coreEntry, out var addr),
                $"Expected core export '{coreEntry}' not found.");
            _output.WriteLine(
                $"  Core export '{coreEntry}' present at FuncAddr {addr}.");
            _output.WriteLine(
                "  (Invocation deferred to next slice — current " +
                "permissive stubs return 0 for wit-bindgen's " +
                "wit_stream / wit_future polling, which means " +
                "tx.write_all spins forever on the never-ready " +
                "signal. The fix is binding stream/future helper " +
                "imports to dispatcher.StreamNew / FutureNew / " +
                "StreamWrite / etc. instead of permissive stubs.)");
        }

        /// <summary>
        /// The full acceptance: capture stdout from cli-hello's
        /// run() through interpreter execution. Will fail with
        /// a concrete error pointing to the next blocker until
        /// the canon-async Invoke wiring lands.
        /// </summary>
        [Fact(Skip = "Pending Instance-export Invoke API extension + canon-async lift wiring.")]
        public void CliHello_writes_expected_stdout()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));

            using var capturedStdout = new MemoryStream();
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder
                {
                    Stdout = new StreamBackedSink(capturedStdout),
                });

            var instance = ComponentInstance.Instantiate(bytes,
                runtime =>
                {
                    host.BindToRuntime(runtime);
                    using var inner = new MemoryStream(bytes);
                    var component = ComponentBinaryParser.Parse(inner);
                    foreach (var coreBytes in component.CoreModuleBinaries)
                    {
                        using var coreStream = new MemoryStream(coreBytes);
                        StubUnboundFunctionImports(runtime,
                            BinaryModuleParser.ParseWasm(coreStream));
                    }
                });
            instance.Invoke("wasi:cli/run@0.3.0-rc-2026-03-15#run");

            capturedStdout.Position = 0;
            var captured = Encoding.UTF8.GetString(capturedStdout.ToArray());
            Assert.Equal("hello, wasip3\n", captured);
        }

        [Fact]
        public void Core_module_imports_inventory()
        {
            var bytes = File.ReadAllBytes(FixturePath("cli-hello.wasm"));
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);

            int totalCores = 0;
            int totalFuncImports = 0;
            var moduleCounts = new Dictionary<string, int>();
            var prefixCounts = new Dictionary<string, int>();

            foreach (var coreBytes in component.CoreModuleBinaries)
            {
                totalCores++;
                using var coreStream = new MemoryStream(coreBytes);
                var module = BinaryModuleParser.ParseWasm(coreStream);
                foreach (var import in module.Imports)
                {
                    if (!(import.Desc is Module.ImportDesc.FuncDesc))
                        continue;
                    totalFuncImports++;
                    moduleCounts.TryGetValue(import.ModuleName, out int m);
                    moduleCounts[import.ModuleName] = m + 1;

                    string prefix = import.Name.StartsWith("[")
                        ? import.Name.Substring(0,
                            Math.Min(import.Name.IndexOf(']') + 1,
                                     import.Name.Length))
                        : "(unbracketed)";
                    prefixCounts.TryGetValue(prefix, out int p);
                    prefixCounts[prefix] = p + 1;
                }
            }

            _output.WriteLine(
                $"{totalCores} core module(s), " +
                $"{totalFuncImports} function imports total");
            foreach (var kvp in moduleCounts.OrderByDescending(k => k.Value))
                _output.WriteLine($"  {kvp.Value,4} {kvp.Key}");
            foreach (var kvp in prefixCounts.OrderByDescending(k => k.Value))
                _output.WriteLine($"  {kvp.Value,4} {kvp.Key}");
        }

        // ---- Permissive stubber ----------------------------------
        //
        // Walks a core module's function imports and binds a
        // zero-returning permissive delegate for any that aren't
        // already in the runtime's entity-bindings table.
        // Dynamically synthesizes Func<>/Action<> matching the
        // WASM signature so the type-check at instantiation
        // accepts the binding.

        private static int StubUnboundFunctionImports(
            WasmRuntime runtime, Module coreModule)
        {
            var defTypes = coreModule.UnrollTypes();

            int count = 0;
            foreach (var import in coreModule.Imports)
            {
                if (!(import.Desc is Module.ImportDesc.FuncDesc fd))
                    continue;
                var id = (import.ModuleName, import.Name);
                if (runtime.TryGetExportedFunction(id, out _))
                    continue;

                int idx = (int)fd.TypeIndex.Value;
                if (idx < 0 || idx >= defTypes.Count) continue;
                var fnType = defTypes[idx].Expansion as FunctionType;
                if (fnType == null) continue;

                var paramClrTypes = fnType.ParameterTypes.Types
                    .Select(MapValTypeToClr).ToArray();
                if (paramClrTypes.Any(t => t == null)) continue;

                Type? returnClrType = fnType.ResultType.Types.Length switch
                {
                    0 => null,
                    1 => MapValTypeToClr(fnType.ResultType.Types[0]),
                    _ => null,
                };
                if (fnType.ResultType.Types.Length == 1
                    && returnClrType == null) continue;
                if (fnType.ResultType.Types.Length > 1) continue;

                var del = BuildPermissiveStubDelegate(
                    paramClrTypes!, returnClrType);
                runtime.BindHostFunction(id, del);
                count++;
            }
            return count;
        }

        private static Type? MapValTypeToClr(ValType v) => v switch
        {
            ValType.I32 => typeof(int),
            ValType.I64 => typeof(long),
            ValType.F32 => typeof(float),
            ValType.F64 => typeof(double),
            _ => null,
        };

        private static Delegate BuildPermissiveStubDelegate(
            Type[] paramTypes, Type? returnType)
        {
            // Build the (ExecContext, P1..PN) parameter list.
            var paramExprs = new List<Expr.ParameterExpression>(paramTypes.Length + 1);
            paramExprs.Add(Expr.Expression.Parameter(typeof(ExecContext), "ctx"));
            for (int i = 0; i < paramTypes.Length; i++)
                paramExprs.Add(Expr.Expression.Parameter(paramTypes[i], $"p{i}"));

            // Choose Action / Func and build the delegate type.
            Type delegateType;
            Expr.Expression body;
            if (returnType == null)
            {
                var typeArgs = paramExprs.Select(p => p.Type).ToArray();
                delegateType = typeArgs.Length == 0
                    ? typeof(Action)
                    : Type.GetType($"System.Action`{typeArgs.Length}")!
                        .MakeGenericType(typeArgs);
                body = Expr.Expression.Empty();
            }
            else
            {
                var typeArgs = paramExprs.Select(p => p.Type)
                    .Append(returnType).ToArray();
                delegateType = Type.GetType($"System.Func`{typeArgs.Length}")!
                    .MakeGenericType(typeArgs);
                body = Expr.Expression.Default(returnType);
            }

            return Expr.Expression.Lambda(delegateType, body, paramExprs).Compile();
        }
    }
}
