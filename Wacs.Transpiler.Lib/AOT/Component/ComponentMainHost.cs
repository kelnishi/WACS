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
        /// </summary>
        public static int Run(Type moduleClass, string[] args,
            string exportName)
        {
            if (moduleClass == null) throw new ArgumentNullException(nameof(moduleClass));

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
                    (bundle, resources) = BuildWasip2BundleAndResources();

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
                    + "cannot dispatch '" + exportName + "'.");

            var sanitized = SanitizeExportName(exportName);
            var method = exportsInterface.GetMethod(sanitized)
                ?? exportsInterface.GetMethod(exportName)
                ?? throw new InvalidOperationException(
                    "Export '" + exportName + "' (sanitized '" + sanitized
                    + "') not found on " + exportsInterface.FullName + ".");

            // Parse argv into the export's CLR param types. v0
            // covers primitives (i32/u32/i64/u64/f32/f64 + narrow
            // ints + bool), strings (passed verbatim), and byte[]
            // (UTF-8 encoded). Aggregate component-shape params
            // (Option<T>, list<T>, records) ride later — they need
            // an argv grammar (today: positional args).
            var pars = method.GetParameters();
            if (args.Length < pars.Length)
                throw new InvalidOperationException(
                    "Export '" + sanitized + "' expects "
                    + pars.Length + " argument(s); got " + args.Length + ".");
            var parsedArgs = new object?[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                parsedArgs[i] = ParseArg(args[i], pars[i].ParameterType,
                    sanitized, i);

            object? result = method.Invoke(instance, parsedArgs);

            return RenderResult(result);
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
        private static (object bundle, object? resources)
            BuildWasip2BundleAndResources()
        {
            var diAsmName = "Wacs.WASI.Preview2.DependencyInjection";
            Assembly diAsm;
            try { diAsm = Assembly.Load(diAsmName); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Component --emit-main needs " + diAsmName
                    + ".dll on the load path. " + ex.Message);
            }

            var bundleType = diAsm.GetType(
                diAsmName + ".WasiPreview2Bundle")
                ?? throw new InvalidOperationException(
                    "Could not find WasiPreview2Bundle in " + diAsmName);
            var resourcesType = diAsm.GetType(
                diAsmName + ".WasiPreview2Resources");
            var extType = diAsm.GetType(
                diAsmName + ".WasiPreview2ServiceCollectionExtensions")
                ?? throw new InvalidOperationException(
                    "Could not find WasiPreview2ServiceCollectionExtensions in " + diAsmName);

            var mediAsm = Assembly.Load("Microsoft.Extensions.DependencyInjection");
            var mediAbstractionsAsm = Assembly.Load("Microsoft.Extensions.DependencyInjection.Abstractions");

            var serviceCollectionType = mediAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceCollection")!;
            var iServiceCollectionType = mediAbstractionsAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection")!;

            var services = Activator.CreateInstance(serviceCollectionType)!;

            var addMethod = extType.GetMethod("AddWasiPreview2",
                BindingFlags.Public | BindingFlags.Static)!;
            addMethod.Invoke(null, new object?[] { services, null });

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

            var bundle = getRequired.MakeGenericMethod(bundleType)
                .Invoke(null, new object?[] { scopeSp })!;
            object? resources = null;
            if (resourcesType != null)
                resources = getRequired.MakeGenericMethod(resourcesType)
                    .Invoke(null, new object?[] { scopeSp });

            return (bundle, resources);
        }
    }
}
