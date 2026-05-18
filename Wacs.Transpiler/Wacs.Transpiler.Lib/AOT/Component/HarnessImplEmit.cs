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
using Wacs.ComponentModel.CSharpEmit;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Emits the <c>{World}HarnessImpl</c> wrapper class that
    /// implements the harness's <c>I{World}</c> interface by
    /// forwarding each method to the transpiler-emitted
    /// <c>ComponentExports</c>'s static methods. The result: an
    /// embedder can construct <c>new {World}HarnessImpl()</c> and
    /// program against the harness <c>I{World}</c> — engine choice
    /// (interpreter via <c>{World}Harness.LoadFrom</c> vs
    /// transpiler via this wrapper) is a deployment detail, per
    /// <c>feedback_symmetric_engines</c>.
    ///
    /// <para>v0 scope: one wrapper class per top-level
    /// <c>I{World}</c> interface. Method matching is by PascalCase
    /// name + signature shape — both sides used the same WIT type
    /// emit pass (the harness's named types were pre-registered
    /// with ComponentExportsEmit), so signatures align without
    /// translation glue.</para>
    /// </summary>
    internal static class HarnessImplEmit
    {
        /// <summary>
        /// Build the <c>{World}HarnessImpl</c> class. No-op when
        /// either the harness binder lacks an <c>I{World}</c>
        /// interface or <paramref name="componentExports"/> is
        /// null (component has no exports to wrap).
        /// </summary>
        public static Type? Emit(
            ModuleBuilder module,
            string @namespace,
            HarnessAssemblyBinder binder,
            Type? componentExports)
        {
            if (binder.WorldInterface == null) return null;
            if (componentExports == null) return null;

            var iface = binder.WorldInterface;
            var typeName = @namespace + "." + iface.Name.TrimStart('I') + "HarnessImpl";

            var tb = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                parent: typeof(object),
                interfaces: new[] { iface });

            // Public parameterless ctor — required so embedders can
            // `new {World}HarnessImpl()` directly.
            var ctor = tb.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.HasThis,
                Type.EmptyTypes);
            var cil = ctor.GetILGenerator();
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            cil.Emit(OpCodes.Ret);

            // Per interface method: emit a forwarding instance method
            // that calls the matching static method on
            // ComponentExports. Method matching is by simple name
            // + arity + parameter type identity. Methods that don't
            // line up (e.g. ComponentExports skipped emit for an
            // unsupported shape) get an instance method that
            // throws NotImplementedException — keeps the type
            // structurally complete for IL2 verification, surfaces
            // the gap at first call.
            foreach (var ifaceMethod in iface.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                EmitForwardingMethod(tb, ifaceMethod, componentExports);
            }

            return tb.CreateType();
        }

        private static void EmitForwardingMethod(
            TypeBuilder tb, MethodInfo ifaceMethod, Type componentExports)
        {
            var paramTypes = ifaceMethod.GetParameters()
                .Select(p => p.ParameterType).ToArray();
            var returnType = ifaceMethod.ReturnType;

            var method = tb.DefineMethod(
                ifaceMethod.Name,
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.NewSlot,
                returnType,
                paramTypes);

            // Carry parameter names through for embedder ergonomics.
            var ps = ifaceMethod.GetParameters();
            for (int i = 0; i < ps.Length; i++)
                method.DefineParameter(i + 1, ParameterAttributes.None, ps[i].Name);

            var il = method.GetILGenerator();

            // Match against ComponentExports's static methods by
            // name + signature shape. The pre-registered-types
            // pass made signatures align — same Vec2 / Outcome
            // CLR types on both sides — so direct match works.
            var target = componentExports.GetMethod(
                ifaceMethod.Name,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: paramTypes,
                modifiers: null);

            if (target == null)
            {
                // No matching static — emit a throw so the build
                // still completes (the interface contract is
                // structurally satisfied), but the missing
                // implementation surfaces loudly at the first call.
                il.Emit(OpCodes.Ldstr,
                    "HarnessImpl: ComponentExports has no matching static method for '"
                    + ifaceMethod.Name + "'.");
                il.Emit(OpCodes.Newobj,
                    typeof(NotImplementedException).GetConstructor(new[] { typeof(string) })!);
                il.Emit(OpCodes.Throw);
                return;
            }

            // Forward: push each arg, call the static, return.
            for (int i = 0; i < paramTypes.Length; i++)
                EmitLdarg(il, i + 1);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
        }

        private static void EmitLdarg(ILGenerator il, int idx)
        {
            switch (idx)
            {
                case 0: il.Emit(OpCodes.Ldarg_0); break;
                case 1: il.Emit(OpCodes.Ldarg_1); break;
                case 2: il.Emit(OpCodes.Ldarg_2); break;
                case 3: il.Emit(OpCodes.Ldarg_3); break;
                default:
                    if (idx <= byte.MaxValue) il.Emit(OpCodes.Ldarg_S, (byte)idx);
                    else il.Emit(OpCodes.Ldarg, (short)idx);
                    break;
            }
        }
    }
}
