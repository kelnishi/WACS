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

            // Detect ComponentExports's shape via its public ctors.
            // Static class → no public ctors → HarnessImpl ctor is
            // parameterless and forwards to static methods.
            // Instance class → exactly one public ctor taking
            // IImports → HarnessImpl ctor takes the same IImports
            // and constructs a held ComponentExports instance.
            var ceCtors = componentExports.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance);
            bool ceIsInstance = ceCtors.Length > 0;
            Type? importsType = ceIsInstance
                ? ceCtors[0].GetParameters()[0].ParameterType : null;

            var tb = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                parent: typeof(object),
                interfaces: new[] { iface });

            FieldBuilder? ceField = null;
            if (ceIsInstance)
            {
                ceField = tb.DefineField(
                    "_componentExports", componentExports,
                    FieldAttributes.Private | FieldAttributes.InitOnly);
            }

            // Ctor shape mirrors ComponentExports's. Static CE →
            // parameterless HarnessImpl ctor (back-compat with
            // embedders that just do `new {World}HarnessImpl()`).
            // Instance CE → ctor takes IImports, constructs CE,
            // stores in _componentExports.
            var ctorParams = ceIsInstance ? new[] { importsType! } : Type.EmptyTypes;
            var ctor = tb.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                CallingConventions.HasThis,
                ctorParams);
            if (ceIsInstance)
                ctor.DefineParameter(1, ParameterAttributes.None, "imports");
            var cil = ctor.GetILGenerator();
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            if (ceIsInstance)
            {
                // _componentExports = new ComponentExports(imports);
                cil.Emit(OpCodes.Ldarg_0);
                cil.Emit(OpCodes.Ldarg_1);
                cil.Emit(OpCodes.Newobj, ceCtors[0]);
                cil.Emit(OpCodes.Stfld, ceField!);
            }
            cil.Emit(OpCodes.Ret);

            // Per interface method: emit a forwarding instance method.
            // Static CE → call the matching static method. Instance
            // CE → load _componentExports, callvirt the matching
            // instance method. Methods that don't line up emit a
            // NotImplementedException-throwing body.
            foreach (var ifaceMethod in iface.GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                EmitForwardingMethod(tb, ifaceMethod, componentExports,
                    ceField);
            }

            return tb.CreateType();
        }

        private static void EmitForwardingMethod(
            TypeBuilder tb, MethodInfo ifaceMethod, Type componentExports,
            FieldBuilder? ceField)
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

            // Match against ComponentExports by name + signature
            // shape. Static CE → look up static method, Call.
            // Instance CE → look up instance method, load
            // _componentExports, Callvirt. The pre-registered-types
            // pass made signatures align — same Vec2 / Outcome
            // CLR types on both sides — so direct match works in
            // either shape.
            var bindingFlags = BindingFlags.Public
                | (ceField == null ? BindingFlags.Static : BindingFlags.Instance);
            var target = componentExports.GetMethod(
                ifaceMethod.Name,
                bindingFlags,
                binder: null,
                types: paramTypes,
                modifiers: null);

            if (target == null)
            {
                // No match — emit a throw so the build still
                // completes (the interface contract is structurally
                // satisfied), but the missing implementation
                // surfaces loudly at the first call.
                il.Emit(OpCodes.Ldstr,
                    "HarnessImpl: ComponentExports has no matching "
                    + (ceField == null ? "static" : "instance")
                    + " method for '" + ifaceMethod.Name + "'.");
                il.Emit(OpCodes.Newobj,
                    typeof(NotImplementedException).GetConstructor(new[] { typeof(string) })!);
                il.Emit(OpCodes.Throw);
                return;
            }

            if (ceField != null)
            {
                // Instance CE: load _componentExports first.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ceField);
            }
            // Forward: push each arg, call the target.
            for (int i = 0; i < paramTypes.Length; i++)
                EmitLdarg(il, i + 1);
            il.Emit(ceField == null ? OpCodes.Call : OpCodes.Callvirt, target);
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
