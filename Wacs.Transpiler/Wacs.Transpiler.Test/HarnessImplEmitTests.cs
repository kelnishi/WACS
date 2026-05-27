// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Harness.Lib;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Verifies the cross-engine symmetry payoff: when the
    /// transpiler is given a harness assembly via
    /// <see cref="TranspilerOptions.HarnessAssemblyPath"/>, it
    /// emits a <c>{World}HarnessImpl</c> wrapper class that
    /// implements the harness's <c>I{World}</c> interface by
    /// forwarding to <c>ComponentExports</c>'s static methods.
    /// </summary>
    public class HarnessImplEmitTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return dir!.FullName;
        }

        private static string FixtureDir(string name)
            => Path.Combine(RepoRoot(), "Spec.Test", "components",
                            "fixtures", name);

        [Fact]
        public void Primitives_transpile_with_harness_emits_HarnessImpl_implementing_IStats()
        {
            // The primitives fixture is import-free (cargo bundles
            // no WASI when the WIT has no imports), so the
            // transpiler's "Imports-less components only" gate in
            // EmitComponentExportsClass passes. Exercises the full
            // pipeline: HarnessAssemblyBinder → preRegisteredTypes
            // seed → ComponentExports → HarnessImpl.
            var fixtureDir = FixtureDir("wit-harness-spike-primitives");
            var witDir = Path.Combine(fixtureDir, "wit");
            var componentWasm = Path.Combine(fixtureDir, "wasm", "stats.component.wasm");

            // 1. Build the harness .dll from the fixture's WIT.
            var tmpHarness = Path.Combine(Path.GetTempPath(),
                $"stats-harness-{Guid.NewGuid():N}.dll");
            try
            {
                HarnessEmitter.EmitToFile(witDir, tmpHarness);

                // 2a. Diagnostic: inspect the parsed component's exports
                //     before transpiling so we can see what the emitter
                //     should pick up.
                string exportDiag;
                using (var parseStream = File.OpenRead(componentWasm))
                {
                    var parsed = ComponentTranspiler.Parse(parseStream);
                    var component = parsed.Component;
                    var debug = component.Exports
                        .Where(e => e.Sort == Wacs.ComponentModel.Runtime.Parser.ComponentSort.Func)
                        .Select(e =>
                        {
                            if (!component.ComponentFuncToCanon.TryGetValue(e.Index, out var lift))
                                return $"{e.Name}: no canon-lift map";
                            if (lift.TypeIdx >= component.Types.Count)
                                return $"{e.Name}: lift TypeIdx out of range";
                            if (!(component.Types[(int)lift.TypeIdx]
                                is Wacs.ComponentModel.Runtime.Parser.ComponentFuncType fn))
                                return $"{e.Name}: type isn't ComponentFuncType";
                            var paramShapes = string.Join(",",
                                fn.Params.Select(p => p.Type.IsPrimitive
                                    ? $"prim:{p.Type.Prim}"
                                    : $"typeIdx:{p.Type.TypeIdx}"));
                            var resultShapes = string.Join(",",
                                fn.Results.Select(r => r.IsPrimitive
                                    ? $"prim:{r.Prim}"
                                    : $"typeIdx:{r.TypeIdx}"));
                            return $"{e.Name}: params=[{paramShapes}] results=[{resultShapes}]";
                        })
                        .ToArray();
                    exportDiag = string.Join(" | ", debug);
                }

                // 2. Transpile the component with the harness wired in.
                //    The hello fixture's core module imports a fixed
                //    set of wasi:io / wasi:cli resource-drop + stdio
                //    functions even though `greet` never calls them
                //    (the wasm32-wasip2 target bundles them); stub
                //    them with throws so instantiate doesn't block on
                //    unbound imports.
                using var fs = File.OpenRead(componentWasm);
                var result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: "Wacs.Transpiled.Stats",
                    options: new TranspilerOptions
                    {
                        HarnessAssemblyPath = tmpHarness,
                    });

                // 3. The transpiled assembly should contain
                //    HelloHarnessImpl, and it should implement the
                //    harness's IHello interface.
                var allTypes = result.Assembly.GetTypes()
                    .Select(t => t.FullName).ToArray();
                var harnessImpl = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "StatsHarnessImpl");
                Assert.True(harnessImpl != null,
                    "StatsHarnessImpl not found. Export diagnostic: "
                    + exportDiag + ". Emitted types: "
                    + string.Join(", ", allTypes));

                var harnessAsm = Assembly.LoadFrom(tmpHarness);
                var iStats = harnessAsm.GetTypes()
                    .FirstOrDefault(t => t.Name == "IStats" && t.IsInterface);
                Assert.NotNull(iStats);

                Assert.True(iStats!.IsAssignableFrom(harnessImpl),
                    $"{harnessImpl!.FullName} must implement {iStats.FullName}.");
            }
            finally
            {
                try { File.Delete(tmpHarness); } catch { }
            }
        }

        [Fact]
        public void Hello_transpile_with_harness_emits_HarnessImpl_implementing_IHello()
        {
            // wit-harness-spike-hello has WASI imports — the Module
            // ctor takes IImports. Verifies the instance-shape
            // ComponentExports path: HarnessImpl gains a (IImports)
            // ctor; export methods become instance, callvirt'd off
            // _componentExports.
            var fixtureDir = FixtureDir("wit-harness-spike-hello");
            var witDir = Path.Combine(fixtureDir, "wit");
            var componentWasm = Path.Combine(fixtureDir, "wasm", "hello.component.wasm");

            var tmpHarness = Path.Combine(Path.GetTempPath(),
                $"hello-harness-{Guid.NewGuid():N}.dll");
            try
            {
                HarnessEmitter.EmitToFile(witDir, tmpHarness);
                using var fs = File.OpenRead(componentWasm);
                var result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: "Wacs.Transpiled.Hello",
                    options: new TranspilerOptions
                    {
                        HarnessAssemblyPath = tmpHarness,
                    },
                    configureImports: BindHelloWasiStubs);

                var allTypes = result.Assembly.GetTypes()
                    .Select(t => t.FullName).ToArray();
                var harnessImpl = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "HelloHarnessImpl");
                Assert.True(harnessImpl != null,
                    "HelloHarnessImpl not found. Emitted types: "
                    + string.Join(", ", allTypes));

                var harnessAsm = Assembly.LoadFrom(tmpHarness);
                var iHello = harnessAsm.GetTypes()
                    .FirstOrDefault(t => t.Name == "IHello" && t.IsInterface);
                Assert.NotNull(iHello);

                Assert.True(iHello!.IsAssignableFrom(harnessImpl),
                    $"{harnessImpl!.FullName} must implement {iHello.FullName}.");

                // HarnessImpl should now have an (IImports) ctor —
                // verify by looking up a public ctor with one arg
                // and confirming the arg is an interface (IImports).
                var ctors = harnessImpl.GetConstructors();
                Assert.Single(ctors);
                var ctorParams = ctors[0].GetParameters();
                Assert.Single(ctorParams);
                Assert.True(ctorParams[0].ParameterType.IsInterface,
                    "HarnessImpl ctor's single arg should be the IImports interface.");
            }
            finally
            {
                try { File.Delete(tmpHarness); } catch { }
            }
        }

        [Fact]
        public void Richer_transpile_with_harness_emits_HarnessImpl_implementing_IRicher()
        {
            // wit-harness-spike-richer exports add(a: vec2, b: vec2) -> vec2
            // and normalize-or-fail(v: vec2) -> outcome. Record params
            // lower as a single ptr each via cabi_realloc + per-field
            // PrimitiveStore.Store calls. Acceptance: RicherHarnessImpl
            // emits and implements IRicher.
            var fixtureDir = FixtureDir("wit-harness-spike-richer");
            var witDir = Path.Combine(fixtureDir, "wit");
            var componentWasm = Path.Combine(fixtureDir, "wasm", "richer.component.wasm");

            var tmpHarness = Path.Combine(Path.GetTempPath(),
                $"richer-harness-{Guid.NewGuid():N}.dll");
            try
            {
                HarnessEmitter.EmitToFile(witDir, tmpHarness);
                using var fs = File.OpenRead(componentWasm);
                var result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: "Wacs.Transpiled.Richer",
                    options: new TranspilerOptions
                    {
                        HarnessAssemblyPath = tmpHarness,
                    });

                var allTypes = result.Assembly.GetTypes()
                    .Select(t => t.FullName).ToArray();
                var harnessImpl = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "RicherHarnessImpl");
                Assert.True(harnessImpl != null,
                    "RicherHarnessImpl not found. Emitted types: "
                    + string.Join(", ", allTypes));

                var harnessAsm = Assembly.LoadFrom(tmpHarness);
                var iRicher = harnessAsm.GetTypes()
                    .FirstOrDefault(t => t.Name == "IRicher" && t.IsInterface);
                Assert.NotNull(iRicher);

                Assert.True(iRicher!.IsAssignableFrom(harnessImpl),
                    $"{harnessImpl!.FullName} must implement {iRicher.FullName}.");
            }
            finally
            {
                try { File.Delete(tmpHarness); } catch { }
            }
        }

        [Fact]
        public void EnumFlags_transpile_with_harness_emits_HarnessImpl_implementing_ISecurity()
        {
            // wit-harness-spike-enum-flags exports get-status returning
            // status { sev: severity, perms: permissions } where the
            // fields are an enum and a flags type. ComponentExportsEmit
            // now accepts records whose fields are primitives, enums,
            // or flags via TryResolveRecordOfFlat + the
            // EmitPrimCtorArgConv enum branch.
            var fixtureDir = FixtureDir("wit-harness-spike-enum-flags");
            var witDir = Path.Combine(fixtureDir, "wit");
            var componentWasm = Path.Combine(fixtureDir, "wasm", "security.component.wasm");

            var tmpHarness = Path.Combine(Path.GetTempPath(),
                $"sec-harness-{Guid.NewGuid():N}.dll");
            try
            {
                HarnessEmitter.EmitToFile(witDir, tmpHarness);
                using var fs = File.OpenRead(componentWasm);
                var result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: "Wacs.Transpiled.Sec",
                    options: new TranspilerOptions
                    {
                        HarnessAssemblyPath = tmpHarness,
                    });

                var allTypes = result.Assembly.GetTypes()
                    .Select(t => t.FullName).ToArray();
                var harnessImpl = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "SecurityHarnessImpl");
                Assert.True(harnessImpl != null,
                    "SecurityHarnessImpl not found. Emitted types: "
                    + string.Join(", ", allTypes));

                var harnessAsm = Assembly.LoadFrom(tmpHarness);
                var iSecurity = harnessAsm.GetTypes()
                    .FirstOrDefault(t => t.Name == "ISecurity" && t.IsInterface);
                Assert.NotNull(iSecurity);

                Assert.True(iSecurity!.IsAssignableFrom(harnessImpl),
                    $"{harnessImpl!.FullName} must implement {iSecurity.FullName}.");
            }
            finally
            {
                try { File.Delete(tmpHarness); } catch { }
            }
        }

        [Theory]
        [InlineData("tiny-component", "tiny.component.wasm", "tiny", "tiny.wit")]
        [InlineData("option-none-component", "n.component.wasm", "n", "n.wit")]
        [InlineData("tuple-return-component", "t.component.wasm", "t", "t.wit")]
        [InlineData("result-return-component", "r.component.wasm", "r", "r.wit")]
        public void Local_fixture_transpile_with_harness_emits_HarnessImpl(
            string fixtureName, string componentFile, string worldName,
            string witFile)
        {
            // Local fixtures (no WASI imports, hand-written WITs).
            // Each exercises a different return shape: tiny primitive,
            // option<u32>, tuple<u32, u32>, result<u32, u32>. All have
            // no params so they sit cleanly within the existing
            // IsEmittable surface. Acceptance: the emitted
            // {World}HarnessImpl is assignable to the harness's
            // I{World} interface.
            var fixtureDir = FixtureDir(fixtureName);
            var witDir = Path.Combine(fixtureDir, "wit");
            if (!Directory.Exists(witDir))
            {
                // Some fixtures predate the harness path; skip cleanly.
                return;
            }
            var componentWasm = Path.Combine(fixtureDir, "wasm", componentFile);
            if (!File.Exists(componentWasm))
            {
                return;
            }
            _ = witFile;

            var camelWorld = char.ToUpperInvariant(worldName[0]) + worldName.Substring(1);
            var tmpHarness = Path.Combine(Path.GetTempPath(),
                $"{worldName}-harness-{Guid.NewGuid():N}.dll");
            try
            {
                HarnessEmitter.EmitToFile(witDir, tmpHarness);

                using var fs = File.OpenRead(componentWasm);
                var result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: "Wacs.Transpiled.Local" + camelWorld,
                    options: new TranspilerOptions
                    {
                        HarnessAssemblyPath = tmpHarness,
                    });

                var allTypes = result.Assembly.GetTypes()
                    .Select(t => t.FullName).ToArray();
                var harnessImpl = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == camelWorld + "HarnessImpl");
                Assert.True(harnessImpl != null,
                    $"{camelWorld}HarnessImpl not found. Emitted types: "
                    + string.Join(", ", allTypes));

                var harnessAsm = Assembly.LoadFrom(tmpHarness);
                var iWorld = harnessAsm.GetTypes()
                    .FirstOrDefault(t => t.Name == "I" + camelWorld && t.IsInterface);
                Assert.NotNull(iWorld);

                Assert.True(iWorld!.IsAssignableFrom(harnessImpl),
                    $"{harnessImpl!.FullName} must implement {iWorld.FullName}.");
            }
            finally
            {
                try { File.Delete(tmpHarness); } catch { }
            }
        }

        [Fact]
        public void InterfaceExport_transpile_with_harness_emits_HarnessImpl()
        {
            // wit-harness-spike-interface-export: a world that
            // exports an interface (ops { add; swap }) alongside a
            // world-level free function (bake). All u32-only signatures,
            // no records, no aggregates — exercises the harness-impl
            // pipeline against the simplest primitive-export shape.
            var fixtureDir = FixtureDir("wit-harness-spike-interface-export");
            var witDir = Path.Combine(fixtureDir, "wit");
            var componentWasm = Path.Combine(fixtureDir, "wasm", "calculator.component.wasm");

            var tmpHarness = Path.Combine(Path.GetTempPath(),
                $"calc-harness-{Guid.NewGuid():N}.dll");
            try
            {
                HarnessEmitter.EmitToFile(witDir, tmpHarness);

                using var fs = File.OpenRead(componentWasm);
                var result = ComponentTranspiler.TranspileSingleModule(
                    fs,
                    assemblyNamespace: "Wacs.Transpiled.Calc",
                    options: new TranspilerOptions
                    {
                        HarnessAssemblyPath = tmpHarness,
                    });

                var allTypes = result.Assembly.GetTypes()
                    .Select(t => t.FullName).ToArray();
                var harnessImpl = result.Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "CalculatorHarnessImpl");
                Assert.True(harnessImpl != null,
                    "CalculatorHarnessImpl not found. Emitted types: "
                    + string.Join(", ", allTypes));

                var harnessAsm = Assembly.LoadFrom(tmpHarness);
                var iCalculator = harnessAsm.GetTypes()
                    .FirstOrDefault(t => t.Name == "ICalculator" && t.IsInterface);
                Assert.NotNull(iCalculator);

                Assert.True(iCalculator!.IsAssignableFrom(harnessImpl),
                    $"{harnessImpl!.FullName} must implement {iCalculator.FullName}.");
            }
            finally
            {
                try { File.Delete(tmpHarness); } catch { }
            }
        }

        // Mirrors HelloHarness.BindWasiStubs in the spike fixture's
        // Aot.Spike project — the hello.component.wasm imports a
        // fixed set of wasi:io / wasi:cli resource-drop + stdio
        // entries the wasm32-wasip2 toolchain bundles unconditionally.
        // None are exercised by `greet`, so throw-stubs suffice.
        private static void BindHelloWasiStubs(WasmRuntime runtime)
        {
            Action<Wacs.Core.Runtime.ExecContext, int> drop =
                (_, _) => throw new NotSupportedException("stub");
            string[] dropTargets =
            {
                "wasi:io/error@0.2.0|[resource-drop]error",
                "wasi:io/poll@0.2.0|[resource-drop]pollable",
                "wasi:io/streams@0.2.0|[resource-drop]input-stream",
                "wasi:io/streams@0.2.0|[resource-drop]output-stream",
                "wasi:cli/terminal-input@0.2.0|[resource-drop]terminal-input",
                "wasi:cli/terminal-output@0.2.0|[resource-drop]terminal-output",
                "wasi:cli/environment@0.2.0|get-environment",
                "wasi:cli/exit@0.2.0|exit",
                "wasi:io/poll@0.2.0|[method]pollable.block",
                "wasi:cli/terminal-stdin@0.2.0|get-terminal-stdin",
                "wasi:cli/terminal-stdout@0.2.0|get-terminal-stdout",
                "wasi:cli/terminal-stderr@0.2.0|get-terminal-stderr",
            };
            foreach (var t in dropTargets)
            {
                var parts = t.Split('|');
                runtime.BindHostFunction<Action<Wacs.Core.Runtime.ExecContext, int>>(
                    (parts[0], parts[1]), drop);
            }

            runtime.BindHostFunction<Action<Wacs.Core.Runtime.ExecContext, int, int>>(
                ("wasi:io/streams@0.2.0", "[method]output-stream.check-write"),
                (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<Wacs.Core.Runtime.ExecContext, int, int>>(
                ("wasi:io/streams@0.2.0", "[method]output-stream.blocking-flush"),
                (_, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Action<Wacs.Core.Runtime.ExecContext, int, int, int, int>>(
                ("wasi:io/streams@0.2.0", "[method]output-stream.write"),
                (_, _, _, _, _) => throw new NotSupportedException("stub"));
            runtime.BindHostFunction<Func<Wacs.Core.Runtime.ExecContext, int, int>>(
                ("wasi:io/streams@0.2.0", "[method]output-stream.subscribe"),
                (_, _) => throw new NotSupportedException("stub"));

            Func<Wacs.Core.Runtime.ExecContext, int> getHandle =
                _ => throw new NotSupportedException("stub");
            runtime.BindHostFunction<Func<Wacs.Core.Runtime.ExecContext, int>>(
                ("wasi:cli/stdin@0.2.0", "get-stdin"), getHandle);
            runtime.BindHostFunction<Func<Wacs.Core.Runtime.ExecContext, int>>(
                ("wasi:cli/stdout@0.2.0", "get-stdout"), getHandle);
            runtime.BindHostFunction<Func<Wacs.Core.Runtime.ExecContext, int>>(
                ("wasi:cli/stderr@0.2.0", "get-stderr"), getHandle);
        }
    }
}
