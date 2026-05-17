// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Harness;
using Wacs.ComponentModel.Types;
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// IL emission for canonical-ABI "lift": reading a typed value out
    /// of wasm linear memory at a known offset. Emits per-type private
    /// static helpers on the harness class — one
    /// <c>Lift{TypeName}(MemoryInstance, int ptr) -&gt; CLR-type</c> for
    /// each named record / variant declared in the world. Per-export
    /// wrappers call into these to lift return values.
    /// </summary>
    internal static class LiftEmit
    {
        private static readonly MethodInfo MemoryHelpers_ReadI32LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadI32LE))!;
        private static readonly MethodInfo MemoryHelpers_ReadU8 =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadU8))!;

        /// <summary>
        /// Walk the world's named types and emit one
        /// <c>Lift{Name}</c> static method per record / variant. Inner
        /// recursive calls (variant payload that's a record, etc.)
        /// link to the same emitted helpers by lookup in the registry.
        /// Returns a map from WIT type name to the emitted lift method.
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, MethodBuilder> EmitLifts(
            TypeBuilder harnessType, CtWorldType world, TypeRegistry registry)
        {
            var liftMethods = new System.Collections.Generic.Dictionary<string, MethodBuilder>();

            // Pass 1: define method signatures so cross-calls resolve.
            foreach (var named in world.Types)
            {
                var structural = CanonicalAbi.Deref(named.Type);
                if (structural is not (CtRecordType or CtVariantType)) continue;
                var clr = WitTypeEmit.MapClrType(structural, registry, $"lift signature for '{named.Name}'");
                var mb = harnessType.DefineMethod(
                    "Lift" + NameMangler.ToPascalCase(named.Name),
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                    clr,
                    new[] { typeof(MemoryInstance), typeof(int) });
                mb.DefineParameter(1, ParameterAttributes.None, "memory");
                mb.DefineParameter(2, ParameterAttributes.None, "ptr");
                liftMethods[named.Name] = mb;
            }

            // Pass 2: fill bodies.
            foreach (var named in world.Types)
            {
                var structural = CanonicalAbi.Deref(named.Type);
                if (!liftMethods.TryGetValue(named.Name, out var mb)) continue;

                var il = mb.GetILGenerator();
                if (structural is CtRecordType rec)
                    EmitRecordLift(il, rec, registry, liftMethods);
                else if (structural is CtVariantType variant)
                    EmitVariantLift(il, variant, registry, liftMethods);
                il.Emit(OpCodes.Ret);
            }

            return liftMethods;
        }

        /// <summary>
        /// Emit IL that lifts a single field-typed value off the stack
        /// at <c>(memory, ptr+offset)</c>. For primitives, inlines the
        /// memory-helper call; for named records / variants, defers to
        /// the corresponding Lift{Name} static via
        /// <paramref name="liftMethods"/>. The emitted code leaves the
        /// CLR value on the stack.
        ///
        /// <para>Assumes <c>memory</c> is in argument slot 0 and
        /// <c>ptr</c> is in slot 1 (the contract every Lift method
        /// follows).</para>
        /// </summary>
        public static void EmitLiftField(
            ILGenerator il, CtValType fieldType, int offset, TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            var deref = CanonicalAbi.Deref(fieldType);
            switch (deref)
            {
                case CtPrimType prim:
                    EmitLiftPrimitive(il, prim, offset);
                    return;

                case CtRecordType rec:
                    if (!liftMethods.TryGetValue(rec.Name, out var recLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for record '{rec.Name}'.");
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, recLift);
                    return;

                case CtVariantType variant:
                    if (!liftMethods.TryGetValue(variant.Name, out var varLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for variant '{variant.Name}'.");
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, varLift);
                    return;

                default:
                    throw new NotSupportedException(
                        $"LiftEmit v0.2 does not support {deref.GetType().Name}.");
            }
        }

        // ===== Inner emitters =====

        private static void EmitRecordLift(
            ILGenerator il, CtRecordType rec, TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            var offsets = CanonicalAbi.RecordFieldOffsets(rec);

            // For each field: push the lifted value onto the stack in
            // declaration order. The record's positional ctor takes
            // them in the same order.
            for (int i = 0; i < rec.Fields.Count; i++)
                EmitLiftField(il, rec.Fields[i].Type, offsets[i], registry, liftMethods);

            // Newobj the record class. Constructor token stashed by
            // WitTypeEmit when it emitted the type.
            il.Emit(OpCodes.Newobj, registry.RecordCtors[rec.Name]);
        }

        private static void EmitVariantLift(
            ILGenerator il, CtVariantType variant, TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            // Strategy: read disc, then a series of compare-and-branch
            // blocks per case. Falls through to throw on unknown disc.
            int payloadOffset = CanonicalAbi.VariantPayloadOffset(variant);
            int discSize = CanonicalAbi.VariantDiscSize(variant.Cases.Count);
            if (discSize != 1)
                throw new NotSupportedException(
                    $"Variant '{variant.Name}' needs disc width {discSize}; v0.2 supports 1-byte discriminators only.");

            var discLocal = il.DeclareLocal(typeof(byte));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
            il.Emit(OpCodes.Stloc, discLocal);

            // Build labels per case + a final "throw on unknown" label.
            var caseLabels = new Label[variant.Cases.Count];
            for (int i = 0; i < variant.Cases.Count; i++)
                caseLabels[i] = il.DefineLabel();
            var defaultLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();  // unused — every case returns via Ret directly

            // Compare-and-branch chain.
            for (int i = 0; i < variant.Cases.Count; i++)
            {
                il.Emit(OpCodes.Ldloc, discLocal);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Beq, caseLabels[i]);
            }
            il.Emit(OpCodes.Br, defaultLabel);

            // Per-case body: lift payload (if any) and newobj subclass.
            var caseCtors = registry.VariantCaseCtors[variant.Name];
            foreach (var (idx, c) in EnumerateIndexed(variant.Cases))
            {
                il.MarkLabel(caseLabels[idx]);
                var ctor = caseCtors[c.Name];
                if (c.Payload != null)
                    EmitLiftField(il, c.Payload, payloadOffset, registry, liftMethods);
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
            }

            // Default: throw InvalidDataException.
            il.MarkLabel(defaultLabel);
            il.Emit(OpCodes.Ldstr, "Unknown variant discriminator for '" + variant.Name + "'.");
            il.Emit(OpCodes.Newobj, typeof(InvalidDataException).GetConstructor(new[] { typeof(string) })!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(endLabel);  // unreachable but keeps the label live.
        }

        private static void EmitLiftPrimitive(ILGenerator il, CtPrimType prim, int offset)
        {
            switch (prim.Kind)
            {
                case CtPrim.S32:
                case CtPrim.U32:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                default:
                    throw new NotSupportedException(
                        $"LiftEmit v0.2 does not yet lift primitive {prim.Kind}.");
            }
        }

        /// <summary>
        /// Push <c>ptr + offset</c> onto the stack. Optimizes the
        /// offset=0 case with a bare <c>ldarg.1</c> to keep the IL
        /// short on the common path (first record field, variant
        /// discriminator read).
        /// </summary>
        private static void EmitOffsetPush(ILGenerator il, int offset)
        {
            il.Emit(OpCodes.Ldarg_1);
            if (offset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, offset);
                il.Emit(OpCodes.Add);
            }
        }

        private static System.Collections.Generic.IEnumerable<(int, T)> EnumerateIndexed<T>(
            System.Collections.Generic.IReadOnlyList<T> items)
        {
            for (int i = 0; i < items.Count; i++) yield return (i, items[i]);
        }
    }
}
