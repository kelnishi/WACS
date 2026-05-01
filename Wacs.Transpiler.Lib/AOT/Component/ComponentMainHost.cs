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
                // crashy null-deref.
                var importsType = ctorParams[0].ParameterType;
                var importsStub = ImportDispatcher.Create(importsType,
                    new Dictionary<string, Func<object?[], object?>>());

                // Second param (when present): host bundle. v0 builds
                // the WASI Preview 2 bundle via the DI extension.
                object? bundle = null;
                if (ctorParams.Length >= 2)
                    bundle = BuildWasiPreview2Bundle();

                instance = ctorParams.Length switch
                {
                    1 => ctor.Invoke(new object?[] { importsStub }),
                    2 => ctor.Invoke(new object?[] { importsStub, bundle }),
                    3 => ctor.Invoke(new object?[] { importsStub, bundle, null }),
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

            // v0: only zero-arg exports are supported. Argv parsing
            // for component-shape params (string, list<u8>, etc.) is
            // a follow-up — the surface is much wider than core-WASM
            // scalars.
            if (method.GetParameters().Length != 0)
                throw new InvalidOperationException(
                    "v0 --emit-main on components only supports zero-argument "
                    + "exports; '" + sanitized + "' takes "
                    + method.GetParameters().Length + " arg(s).");

            object? result = method.Invoke(instance, Array.Empty<object>());

            if (result == null) return 0;
            if (result is int i32) return i32;
            if (result is long i64) return unchecked((int)i64);
            if (result is uint u32) return unchecked((int)u32);
            if (result is ulong u64) return unchecked((int)u64);
            // Non-scalar (string, byte[], record, etc.) — print and
            // return 0. The component-shape print is a follow-up.
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

        private static object BuildWasiPreview2Bundle()
        {
            // Locate the bundle / DI extension via reflection so the
            // transpiler library doesn't carry a hard reference on
            // the WASI Preview 2 packages.
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
            var extType = diAsm.GetType(
                diAsmName + ".WasiPreview2ServiceCollectionExtensions")
                ?? throw new InvalidOperationException(
                    "Could not find WasiPreview2ServiceCollectionExtensions in " + diAsmName);

            // Microsoft.Extensions.DependencyInjection lives in two
            // assemblies — Abstractions for ServiceCollection /
            // ServiceProvider extensions, the main package for the
            // ServiceCollection class itself.
            var mediAsm = Assembly.Load("Microsoft.Extensions.DependencyInjection");
            var mediAbstractionsAsm = Assembly.Load("Microsoft.Extensions.DependencyInjection.Abstractions");

            var serviceCollectionType = mediAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceCollection")!;
            var iServiceCollectionType = mediAbstractionsAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection")!;

            // services = new ServiceCollection();
            var services = Activator.CreateInstance(serviceCollectionType)!;

            // services.AddWasiPreview2();
            var addMethod = extType.GetMethod("AddWasiPreview2",
                BindingFlags.Public | BindingFlags.Static)!;
            addMethod.Invoke(null, new object?[] { services, null });

            // sp = services.BuildServiceProvider();
            var containerExtType = mediAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions")!;
            var buildSp = containerExtType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "BuildServiceProvider"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType
                        == iServiceCollectionType);
            var sp = buildSp.Invoke(null, new object?[] { services })!;

            // bundle = sp.GetRequiredService<WasiPreview2Bundle>();
            var iServiceProviderType = typeof(IServiceProvider);
            var spExtType = mediAbstractionsAsm.GetType(
                "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions")!;
            var getRequired = spExtType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "GetRequiredService"
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType
                        == iServiceProviderType);
            var typed = getRequired.MakeGenericMethod(bundleType);
            return typed.Invoke(null, new object?[] { sp })!;
        }
    }
}
