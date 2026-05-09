// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wacs.HostBindings.Abstractions;
using Wacs.Transpiler.Cli;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Runtime host shim used by <see cref="ComponentMainEntryEmitter"/>.
    /// The emitted <c>Program.Main</c> on a component-mode .dll is a
    /// one-liner: <c>return ComponentMainHost.Run(typeof(Module),
    /// args, "&lt;export&gt;");</c>. This class does the bundle
    /// construction, IImports stub wiring, module instantiation, and
    /// export invocation — keeps the emitted IL trivial and lets the
    /// runtime path evolve without re-emitting.
    ///
    /// <para>v0 scope: only the bundled WASI Preview 2 host
    /// (<c>--wasip2</c>) is supported. The bundle is constructed via
    /// reflection through the <c>Wacs.WASI.Preview2.DependencyInjection</c>
    /// assembly so this library has no compile-time dep on the DI
    /// extension or on Microsoft.Extensions.DependencyInjection.
    /// Other host-package shapes ride later — each needs its own
    /// bundle-construction recipe.</para>
    /// </summary>
    public static class ComponentMainHost
    {
        /// <summary>
        /// Instantiate <paramref name="moduleClass"/> with a WASI
        /// Preview 2 bundle and a no-op IImports stub, then invoke
        /// <paramref name="exportName"/> on its IExports interface.
        /// Returns the export's scalar result (cast to int) when
        /// applicable, otherwise 0.
        ///
        /// <para>When <paramref name="exportName"/> is <c>null</c>
        /// or empty, auto-resolution kicks in: the canonical
        /// command-component entry <c>wasi:cli/run@&lt;version&gt;#run</c>
        /// is matched first (via <c>[WasmName]</c> on the emitted
        /// IExports method), then plain <c>_start</c>, then a
        /// helpful error listing the available exports. This
        /// matches the wasmtime / jco / wasmer behavior where a
        /// stock command component just runs.</para>
        /// </summary>
        public static int Run(Type moduleClass, string[] args,
            string? exportName = null,
            IEnumerable<(string hostPath, string guestPath)>? preopens = null)
        {
            if (moduleClass == null) throw new ArgumentNullException(nameof(moduleClass));

            // Materialize once so the iterator isn't drained on the
            // first reflective use; threaded into
            // BuildWasip2BundleAndResources when non-null + non-empty
            // so the bundle's IPreopens exposes them instead of the
            // empty default.
            var preopenList = preopens?.ToArray();

            var ctor = moduleClass.GetConstructors()[0];
            var ctorParams = ctor.GetParameters();

            // Ctor shape (per ModuleClassGenerator.EmitConstructor):
            //   ()                                    — no imports
            //   (IImports)                            — imports only
            //   (IImports, object hostBundle)         — direct-linked
            //   (IImports, object hostBundle, object) — + resources
            object instance;
            if (ctorParams.Length == 0)
            {
                instance = ctor.Invoke(Array.Empty<object?>());
            }
            else
            {
                // First param: IImports interface — stub with a
                // dispatcher whose handler set is empty. Direct-
                // linked imports never call through this; unresolved
                // ones return defaults from the dispatcher, which
                // surfaces as a downstream failure rather than a
                // crashy null-deref. lenient: true opts out of the
                // dispatcher's default loud-throw behavior because we
                // intentionally want fall-through here.
                var importsType = ctorParams[0].ParameterType;
                var importsStub = ImportDispatcher.Create(importsType,
                    new Dictionary<string, Func<object?[], object?>>(),
                    lenient: true);

                // Bundle (host functions) + resources (handle
                // table bridge) both come from the same DI scope
                // so they share a single ResourceContext — that's
                // the property that lets stdout.get-stdout's
                // returned handle resolve back through
                // streams.[method]output-stream.blocking-write-and-flush.
                object? bundle = null;
                object? resources = null;
                if (ctorParams.Length >= 2)
                    (bundle, resources) = BuildWasip2BundleAndResources(preopenList);

                instance = ctorParams.Length switch
                {
                    1 => ctor.Invoke(new object?[] { importsStub }),
                    2 => ctor.Invoke(new object?[] { importsStub, bundle }),
                    3 => ctor.Invoke(new object?[] { importsStub, bundle, resources }),
                    _ => throw new InvalidOperationException(
                        "Unsupported module ctor arity " + ctorParams.Length),
                };
            }

            // Find the export by sanitized name on IExports.
            var exportsInterface = moduleClass.GetInterfaces()
                .FirstOrDefault(i => i.Name.StartsWith("IExports"))
                ?? throw new InvalidOperationException(
                    "Module class does not implement an IExports interface; "
                    + "cannot dispatch '" + (exportName ?? "<auto>") + "'.");

            MethodInfo method;
            if (string.IsNullOrEmpty(exportName))
            {
                method = AutoResolveCommandEntry(exportsInterface);
            }
            else
            {
                var sanitized = SanitizeExportName(exportName!);
                method = exportsInterface.GetMethod(sanitized)
                    ?? exportsInterface.GetMethod(exportName!)
                    ?? throw new InvalidOperationException(
                        "Export '" + exportName + "' (sanitized '" + sanitized
                        + "') not found on " + exportsInterface.FullName + ".");
            }

            // Parse argv into the export's CLR param types. v0
            // covers primitives (i32/u32/i64/u64/f32/f64 + narrow
            // ints + bool), strings (passed verbatim), and byte[]
            // (UTF-8 encoded). Aggregate component-shape params
            // (Option<T>, list<T>, records) ride later — they need
            // an argv grammar (today: positional args).
            var pars = method.GetParameters();
            if (args.Length < pars.Length)
                throw new InvalidOperationException(
                    "Export '" + method.Name + "' expects "
                    + pars.Length + " argument(s); got " + args.Length + ".");
            var parsedArgs = new object?[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                parsedArgs[i] = ParseArg(args[i], pars[i].ParameterType,
                    method.Name, i);

            object? result;
            try
            {
                result = method.Invoke(instance, parsedArgs);
            }
            catch (TargetInvocationException tie)
                when (IsExitException(tie.InnerException, out byte exitCode))
            {
                // wasi:cli/exit.exit(N) is a terminal divergent
                // control flow — the host translates it into a
                // process-exit code, NOT a generic error. Match
                // wasmtime / jco / wasmer behavior: exit code N
                // becomes our return value, no error message
                // printed (the guest already gets to write to
                // stderr through wasi:cli/stderr if it wants).
                return exitCode;
            }

            return RenderResult(result);
        }

        // Match Wacs.WASI.Preview2.Cli.ExitException by name. We
        // can't reference Preview2 directly here without flipping
        // the dep direction (Transpiler.Lib is below the WASI
        // packages in the layering), so the contract is the
        // type's full name + a `byte ExitCode` property. Any
        // future host package that wants to participate in
        // exit-code propagation declares the same shape.
        private static bool IsExitException(Exception? ex,
            out byte exitCode)
        {
            exitCode = 0;
            if (ex == null) return false;
            var t = ex.GetType();
            if (t.FullName != "Wacs.WASI.Preview2.Cli.ExitException")
                return false;
            var prop = t.GetProperty("ExitCode");
            if (prop == null) return false;
            var raw = prop.GetValue(ex);
            if (raw is byte b) { exitCode = b; return true; }
            if (raw is int i) { exitCode = unchecked((byte)i); return true; }
            return false;
        }

        // Auto-resolve the entry point of a command component when
        // the embedder didn't pass an explicit export name. Prefers
        // the canonical `wasi:cli/run@<version>#run` (matched via
        // [WasmName]); falls back to `_start` if the module is a
        // pre-component WASI Preview 1 binary that ended up on this
        // path. Anything else is an error with a hint.
        private static MethodInfo AutoResolveCommandEntry(
            Type exportsInterface)
        {
            // Pass 1: scan [WasmName] attributes for the
            // `wasi:cli/run@*#run` shape. wit-bindgen-rust,
            // jco/wit-bindgen-js, and the wasm-tools embed/new
            // toolchain all emit this shape for command-world
            // components. Multiple `@<version>` strings can
            // co-exist in principle, but a single component
            // typically declares one — pick the first match.
            foreach (var m in exportsInterface.GetMethods())
            {
                var wn = m.GetCustomAttribute<WasmNameAttribute>();
                if (wn == null) continue;
                if (IsWasiCliRunRunExport(wn.Name))
                    return m;
            }

            // Pass 2: legacy / mis-classified module — try plain
            // `_start`. Preserves backwards compatibility for
            // anything that ended up on the component path despite
            // being a Preview 1 binary.
            var start = exportsInterface.GetMethod("_start");
            if (start != null) return start;

            // Pass 3: surface the available exports so the user
            // knows what to pass to --call.
            var available = string.Join(", ",
                exportsInterface.GetMethods().Select(m =>
                    m.GetCustomAttribute<WasmNameAttribute>()?.Name ?? m.Name));
            throw new InvalidOperationException(
                "No entry point found on " + exportsInterface.FullName + ". "
                + "Expected `wasi:cli/run@<version>#run` (command component) "
                + "or `_start` (Preview 1). Available exports: "
                + (string.IsNullOrEmpty(available) ? "<none>" : available)
                + ". Pass --call <export> to dispatch one explicitly.");
        }

        // Match the wit-bindgen `wasi:cli/run@<version>#run`
        // convention without committing to a specific version.
        // The `wasi:cli/run@` prefix and `#run` suffix together
        // are unique enough — `wasi:cli/run@0.2.0#run`,
        // `wasi:cli/run@0.2.3#run`, etc. all match.
        private static bool IsWasiCliRunRunExport(string exportName)
        {
            return exportName.StartsWith("wasi:cli/run@",
                       StringComparison.Ordinal)
                && exportName.EndsWith("#run", StringComparison.Ordinal);
        }

        // Parse one CLI arg into the expected CLR type. Throws an
        // InvalidOperationException with a clear message on
        // unsupported shapes — the caller surfaces this as the
        // CLI's --run failure exit.
        private static object? ParseArg(string raw, Type t,
            string exportName, int idx)
        {
            try
            {
                if (t == typeof(string)) return raw;
                if (t == typeof(byte[]))
                    return System.Text.Encoding.UTF8.GetBytes(raw);
                if (t == typeof(bool))
                    return bool.Parse(raw);
                if (t == typeof(int))
                    return int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(uint))
                    return uint.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(long))
                    return long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(ulong))
                    return ulong.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(short))
                    return short.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(ushort))
                    return ushort.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(byte))
                    return byte.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(sbyte))
                    return sbyte.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(float))
                    return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(double))
                    return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Could not parse argv[" + idx + "] = '" + raw
                    + "' as " + t.Name + " for export '" + exportName
                    + "': " + ex.Message);
            }
            throw new InvalidOperationException(
                "Export '" + exportName + "' parameter " + idx
                + " has unsupported type " + t.FullName + " for argv "
                + "parsing. Supported: primitives, bool, string, byte[].");
        }

        private static int RenderResult(object? result)
        {
            if (result == null) return 0;
            if (result is int i32) return i32;
            if (result is long i64) return unchecked((int)i64);
            if (result is uint u32) return unchecked((int)u32);
            if (result is ulong u64) return unchecked((int)u64);
            if (result is short i16) return i16;
            if (result is ushort u16) return u16;
            if (result is byte u8) return u8;
            if (result is sbyte i8) return i8;
            if (result is bool b) return b ? 1 : 0;
            if (result is float f32)
            {
                Console.WriteLine(f32.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                return 0;
            }
            if (result is double f64)
            {
                Console.WriteLine(f64.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                return 0;
            }
            if (result is string s)
            {
                Console.WriteLine(s);
                return 0;
            }
            if (result is byte[] bytes)
            {
                Console.Out.Write(System.Text.Encoding.UTF8
                    .GetString(bytes));
                return 0;
            }
            // Aggregate (record / variant / Option / Result / etc.)
            // — fall back to ToString for human inspection. A
            // structured renderer is a follow-up.
            Console.WriteLine(result);
            return 0;
        }

        // Mirror Wacs.Transpiler.AOT.InterfaceGenerator.SanitizeName
        // semantics — wasm exports can contain `:`, `/`, `-`, `@` and
        // dots, none of which are valid C# identifiers. The
        // generator replaces them all with `_`.
        private static string SanitizeExportName(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    chars[i] = '_';
            }
            // Identifiers can't start with a digit.
            if (chars.Length > 0 && char.IsDigit(chars[0]))
                return "_" + new string(chars);
            return new string(chars);
        }

        // Build the bundle + resources pair from a single
        // DI scope so they share a single per-instance
        // ResourceContext. The scope lives for the rest of the
        // process — Main returns shortly after the export
        // invocation. Reflective construction keeps this library
        // free of compile-time refs on Wacs.WASI.Preview2.* and
        // Microsoft.Extensions.DependencyInjection.
        //
        // Multi-bundle: when Wacs.WASI.NN.DependencyInjection is on
        // the load path AND it can register a default ONNX backend
        // (via Wacs.WASI.NN.OnnxRuntime), the host returns a
        // WasiPreview2NNBundle composite that satisfies BOTH the
        // Preview2 and the WASI.NN [WitSource] interfaces through
        // one CLR object. The transpiler's ResolveBundleProperty
        // finds each binding's matching forwarder by name — no
        // emit changes required.
        private static (object bundle, object? resources)
            BuildWasip2BundleAndResources(
                (string hostPath, string guestPath)[]? preopens = null)
        {
            var p2DiAsmName = "Wacs.WASI.Preview2.DependencyInjection";
            Assembly p2DiAsm;
            try { p2DiAsm = Assembly.Load(p2DiAsmName); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Component --emit-main needs " + p2DiAsmName
                    + ".dll on the load path. " + ex.Message);
            }

            var p2BundleType = p2DiAsm.GetType(
                p2DiAsmName + ".WasiPreview2Bundle")
                ?? throw new InvalidOperationException(
                    "Could not find WasiPreview2Bundle in " + p2DiAsmName);
            var resourcesType = p2DiAsm.GetType(
                p2DiAsmName + ".WasiPreview2Resources");
            var p2ExtType = p2DiAsm.GetType(
                p2DiAsmName + ".WasiPreview2ServiceCollectionExtensions")
                ?? throw new InvalidOperationException(
                    "Could not find WasiPreview2ServiceCollectionExtensions in " + p2DiAsmName);

            // Optional WASI.NN.DI — when present, the bundle becomes
            // the composite WasiPreview2NNBundle. Failure to load
            // this assembly is NOT an error: components without
            // wasi-nn imports never need it.
            Assembly? nnDiAsm = TryLoadAssembly(
                "Wacs.WASI.NN.DependencyInjection");
            Type? nnExtType = nnDiAsm?.GetType(
                "Wacs.WASI.NN.DependencyInjection."
                + "WasiNNServiceCollectionExtensions");
            Type? compositeBundleType = nnDiAsm?.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiPreview2NNBundle");

            var mediAsm = Assembly.Load(
                "Microsoft.Extensions.DependencyInjection");
            var mediAbstractionsAsm = Assembly.Load(
                "Microsoft.Extensions.DependencyInjection.Abstractions");

            var serviceCollectionType = mediAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceCollection")!;
            var iServiceCollectionType = mediAbstractionsAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection")!;

            var services = Activator.CreateInstance(serviceCollectionType)!;

            // Register IPreopens BEFORE AddWasiPreview2 so its
            // TryAddSingleton<IPreopens> default registration falls
            // through. Reflection-load the Preview2 Filesystem
            // assembly's `Preopens(IEnumerable<(string, string)>)`
            // ctor — keeps Wacs.Transpiler.Lib free of a compile-
            // time reference to Wacs.WASI.Preview2.Filesystem.
            if (preopens != null && preopens.Length > 0)
            {
                RegisterPreopens(services, p2DiAsm, mediAbstractionsAsm,
                    preopens);
            }

            // AddWasiPreview2(IServiceCollection, Action<...>?) — null
            // accepts defaults.
            p2ExtType.GetMethod("AddWasiPreview2",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services, null });

            // AddWasiNN(IServiceCollection, Action<...>?) when WASI.NN
            // is on the path — registers a default-empty backend
            // configuration; AutoRegisterDefaultBackends below tries
            // to populate it from any loadable backend assembly.
            if (nnExtType != null)
            {
                nnExtType.GetMethod("AddWasiNN",
                        BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { services, null });
                nnExtType.GetMethod("AddWasiPreview2NNBundle",
                        BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object?[] { services });
            }

            var containerExtType = mediAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions")!;
            var buildSp = containerExtType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "BuildServiceProvider"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType
                        == iServiceCollectionType);
            var sp = buildSp.Invoke(null, new object?[] { services })!;

            // Create a scope so InstanceLifetime=Scoped services
            // (ResourceContext, WasiPreview2Resources) resolve.
            var spExtType = mediAbstractionsAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions")!;
            var createScope = spExtType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "CreateScope"
                    && m.GetParameters().Length == 1);
            var scope = createScope.Invoke(null, new object?[] { sp })!;
            var scopeSp = scope.GetType()
                .GetProperty("ServiceProvider")!.GetValue(scope)!;

            var iServiceProviderType = typeof(IServiceProvider);
            var getRequired = spExtType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "GetRequiredService"
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType
                        == iServiceProviderType);

            // Auto-register the ONNX backend with the WASI.NN config
            // when both DI and OnnxRuntime are loaded — saves the
            // embedder a manual `b.AddBackend(...)` call for the
            // common case. Pure no-op when either assembly is
            // missing or the backend type isn't found.
            if (nnDiAsm != null)
                AutoRegisterDefaultBackends(scopeSp, getRequired,
                    nnDiAsm);

            // Resolve the bundle. Composite when WASI.NN is loaded;
            // pure Preview2 otherwise.
            Type bundleType = compositeBundleType ?? p2BundleType;
            var bundle = getRequired.MakeGenericMethod(bundleType)
                .Invoke(null, new object?[] { scopeSp })!;
            object? resources = null;
            if (resourcesType != null)
                resources = getRequired.MakeGenericMethod(resourcesType)
                    .Invoke(null, new object?[] { scopeSp });

            return (bundle, resources);
        }

        private static Assembly? TryLoadAssembly(string name)
        {
            try { return Assembly.Load(name); }
            catch { return null; }
        }

        // Reflectively register an OnnxBackend for graph-encoding.onnx
        // when Wacs.WASI.NN.OnnxRuntime is on the load path. Lets
        // `wacs run --wasip2 --wasi-nn my.component.wasm` work end-
        // to-end without an embedder shim. No-op when the OnnxRuntime
        // assembly isn't loadable.
        private static void AutoRegisterDefaultBackends(object scopeSp,
            MethodInfo getRequired, Assembly nnDiAsm)
        {
            // Get the registered WasiNNConfiguration.
            var configType = nnDiAsm.GetType(
                "Wacs.WASI.NN.WasiNNConfiguration");
            if (configType == null) return;

            object? config;
            try
            {
                config = getRequired.MakeGenericMethod(configType)
                    .Invoke(null, new object?[] { scopeSp });
            }
            catch { return; }
            if (config == null) return;

            var backendsProp = configType.GetProperty("Backends");
            if (backendsProp == null) return;
            var backends = backendsProp.GetValue(config);
            if (backends == null) return;

            // Try to load the OnnxRuntime backend.
            var ortAsm = TryLoadAssembly("Wacs.WASI.NN.OnnxRuntime");
            var onnxBackendType = ortAsm?.GetType(
                "Wacs.WASI.NN.OnnxRuntime.OnnxBackend");
            if (onnxBackendType == null) return;
            object? onnxBackend;
            try { onnxBackend = Activator.CreateInstance(onnxBackendType); }
            catch { return; }
            if (onnxBackend == null) return;

            // GraphEncoding.ONNX = 1 per the WIT enum order — use
            // the underlying integer to avoid hard-typing on the
            // enum here.
            var encodingType = nnDiAsm.GetType(
                "Wacs.WASI.NN.Types.GraphEncoding");
            if (encodingType == null) return;
            object onnxEncoding = Enum.ToObject(encodingType, 1);

            // Backends is IDictionary<GraphEncoding, IBackend> — set
            // via the indexer.
            var indexer = backends.GetType().GetMethod("set_Item");
            indexer?.Invoke(backends,
                new object?[] { onnxEncoding, onnxBackend });
        }

        // Reflectively construct a
        // Wacs.WASI.Preview2.Filesystem.Preopens with the given
        // mount pairs and register it as a singleton IPreopens on
        // the service collection. Runs BEFORE AddWasiPreview2 so
        // the default DefaultPreopens TryAddSingleton falls through.
        private static void RegisterPreopens(object services,
            Assembly p2DiAsm,
            Assembly mediAbstractionsAsm,
            (string hostPath, string guestPath)[] preopens)
        {
            // The Preopens / IPreopens types live in
            // Wacs.WASI.Preview2 (not the DI package); the DI
            // package transitively references it, so the Preview2
            // assembly is loaded by the time we're called.
            var p2Asm = Assembly.Load("Wacs.WASI.Preview2");

            var iPreopensType = p2Asm.GetType(
                "Wacs.WASI.Preview2.Filesystem.IPreopens");
            var preopensType = p2Asm.GetType(
                "Wacs.WASI.Preview2.Filesystem.Preopens");
            if (iPreopensType == null || preopensType == null) return;

            // Find the IEnumerable<(string, string)> ctor.
            var ctor = preopensType.GetConstructors()
                .FirstOrDefault(c =>
                {
                    var ps = c.GetParameters();
                    return ps.Length == 1
                        && ps[0].ParameterType.IsGenericType
                        && ps[0].ParameterType.GetGenericTypeDefinition()
                            == typeof(IEnumerable<>);
                });
            if (ctor == null) return;

            // Materialize the mount pairs as List<ValueTuple<string,string>>
            // — that's the concrete `IEnumerable<(string, string)>`
            // shape the ctor expects.
            var pairs = preopens
                .Select(m => (m.hostPath, m.guestPath))
                .ToList();
            object preopensInstance;
            try { preopensInstance = ctor.Invoke(new object[] { pairs })!; }
            catch { return; }

            // services.AddSingleton(typeof(IPreopens), instance) —
            // use Add (not TryAdd) so we win against the default
            // registration that AddWasiPreview2 would otherwise put
            // in. The two AddSingleton overloads take
            // (IServiceCollection, Type, object) for instance
            // registration; resolve via reflection.
            var addExtType = mediAbstractionsAsm.GetType(
                "Microsoft.Extensions.DependencyInjection."
                + "ServiceCollectionServiceExtensions");
            if (addExtType == null) return;
            var addSingleton = addExtType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "AddSingleton" || m.IsGenericMethod)
                        return false;
                    var ps = m.GetParameters();
                    return ps.Length == 3
                        && ps[1].ParameterType == typeof(Type)
                        && ps[2].ParameterType == typeof(object);
                });
            if (addSingleton == null) return;
            addSingleton.Invoke(null, new object[] {
                services, iPreopensType, preopensInstance });
        }
    }
}
