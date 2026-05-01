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
using System.Reflection.Emit;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Component-mode counterpart to <see cref="MainEntryEmitter"/>.
    /// Emits a <c>public static int Main(string[] args)</c> on a
    /// <c>Program</c> class inside the just-transpiled assembly. The
    /// emitted body is a one-liner forwarding to
    /// <see cref="ComponentMainHost.Run"/> — keeps the IL trivial and
    /// lets the runtime path evolve (broader bundle support, argv
    /// parsing for component-shape params) without re-emitting.
    ///
    /// <para>v0 constraints (matching <see cref="ComponentMainHost"/>):
    /// only <c>--wasip2</c> bundles, only zero-argument exports.
    /// Validates the export exists at emit time so the build fails
    /// fast instead of crashing at runtime.</para>
    /// </summary>
    public static class ComponentMainEntryEmitter
    {
        public static Type Emit(TranspilationResult result,
            string programClassName,
            string exportName,
            IReadOnlyList<Assembly> hostPackages)
        {
            if (result.ModuleClass == null)
                throw new MainEntryEmitter.ConstraintException(
                    "module has no generated Module class; nothing to invoke");

            // v0 contract: components with imports need a bundle —
            // the only one we can build today is WASI Preview 2.
            // No-import components fall through cleanly (the
            // emitted ctor takes a single IImports arg, which
            // ComponentMainHost stubs with a dispatcher).
            bool hasImports = result.ImportsInterface != null
                && result.ImportMethods.Count > 0;
            bool hasWasip2 = hostPackages != null
                && hostPackages.Any(a => string.Equals(
                    a.GetName().Name,
                    "Wacs.WASI.Preview2",
                    StringComparison.Ordinal));
            if (hasImports && !hasWasip2)
                throw new MainEntryEmitter.ConstraintException(
                    "v0 component --emit-main on a component with imports "
                    + "requires --wasip2 (or --host-package Wacs.WASI.Preview2). "
                    + "Custom host packages need their own bundle-construction "
                    + "recipe.");

            // Confirm the export exists. The CLI-provided exportName
            // is the wasm-side name — the IExports method has been
            // sanitized for C# — but ComponentMainHost.Run runs the
            // same sanitization, so we just check the wasm-form is
            // present in result.ExportMethods.
            var export = result.ExportMethods.FirstOrDefault(m =>
                m.WasmName == exportName || m.Name == exportName);
            if (export == null)
            {
                var available = string.Join(", ",
                    result.ExportMethods.Select(m => "\"" + m.WasmName + "\""));
                throw new MainEntryEmitter.ConstraintException(
                    "export '" + exportName + "' not found; available: ["
                    + available + "]");
            }

            // Argv parsing is wired in ComponentMainHost.ParseArg —
            // it covers primitives, bool, string, byte[]. The
            // export's IExports method may take aggregate types we
            // can't parse from argv yet (Option<T>, list<T>,
            // records). Defer the type-shape check to runtime,
            // where ComponentMainHost surfaces a clean error
            // pointing at the offending param. Emit-time check
            // here is unnecessary — looking up the IExports method
            // type happens after the dynamic assembly bakes (the
            // typed signature isn't observable from the
            // ExportMethods metadata).

            var moduleBuilder = result.ModuleBuilder;
            var ns = result.ModuleClass.Namespace;
            var fullName = string.IsNullOrEmpty(ns)
                ? programClassName
                : ns + "." + programClassName;
            var programType = moduleBuilder.DefineType(
                fullName,
                TypeAttributes.Public | TypeAttributes.Sealed
                    | TypeAttributes.Abstract,
                typeof(object));

            var mainMethod = programType.DefineMethod(
                "Main",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                new[] { typeof(string[]) });
            mainMethod.DefineParameter(1, ParameterAttributes.None, "args");

            var il = mainMethod.GetILGenerator();

            // return ComponentMainHost.Run(typeof(<ModuleClass>),
            //                              args, "<exportName>");
            il.Emit(OpCodes.Ldtoken, result.ModuleClass);
            il.Emit(OpCodes.Call,
                typeof(Type).GetMethod(
                    nameof(Type.GetTypeFromHandle),
                    new[] { typeof(RuntimeTypeHandle) })!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, exportName);
            il.Emit(OpCodes.Call,
                typeof(ComponentMainHost).GetMethod(
                    nameof(ComponentMainHost.Run),
                    BindingFlags.Public | BindingFlags.Static)!);
            il.Emit(OpCodes.Ret);

            return programType.CreateType();
        }
    }
}
