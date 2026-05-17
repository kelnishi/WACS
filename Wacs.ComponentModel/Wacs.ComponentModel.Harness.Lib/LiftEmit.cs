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
        private static readonly MethodInfo MemoryHelpers_ReadI16LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadI16LE))!;
        private static readonly MethodInfo MemoryHelpers_ReadI64LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadI64LE))!;
        private static readonly MethodInfo MemoryHelpers_ReadF32LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadF32LE))!;
        private static readonly MethodInfo MemoryHelpers_ReadF64LE =
            typeof(MemoryHelpers).GetMethod(nameof(MemoryHelpers.ReadF64LE))!;
        private static readonly MethodInfo StringCoding_LiftUtf8 =
            typeof(StringCoding).GetMethod(nameof(StringCoding.LiftUtf8))!;

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

                case CtListType list:
                    EmitLiftList(il, list, offset, registry, liftMethods);
                    return;

                case CtEnumType en:
                    {
                        // CLR enums on the stack are their underlying
                        // integer type — no explicit conv between the
                        // integer load and the enum-typed field's
                        // stelem / stfld. Width sized to case count.
                        var width = CanonicalAbi.VariantDiscSize(en.Cases.Count);
                        EmitReadIntegerWidth(il, offset, width);
                        return;
                    }

                case CtFlagsType fl:
                    {
                        var width = CanonicalAbi.FlagsByteWidth(fl.Flags.Count);
                        EmitReadIntegerWidth(il, offset, width);
                        return;
                    }

                default:
                    throw new NotSupportedException(
                        $"LiftEmit v0.2 does not support {deref.GetType().Name}.");
            }
        }

        /// <summary>
        /// Read an unsigned integer at <c>(memory, arg.1 + offset)</c>
        /// sized 1 / 2 / 4 bytes. Used for enum discriminators + flags
        /// backing storage — the CLR enum-typed field's stfld accepts
        /// the matching underlying integer directly.
        /// </summary>
        private static void EmitReadIntegerWidth(ILGenerator il, int offset, int width)
        {
            switch (width)
            {
                case 1:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
                    return;
                case 2:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI16LE);
                    il.Emit(OpCodes.Conv_U2);
                    return;
                case 4:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                default:
                    throw new NotSupportedException($"Unsupported integer width {width}.");
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

        /// <summary>
        /// Emit IL that lifts a <c>list&lt;T&gt;</c> field at
        /// <c>(memory, ptr+offset)</c>. The list itself is stored as
        /// (ptr, count) — two i32s at the field's offset. Delegates
        /// to <see cref="EmitLiftListFromBase"/> which takes the
        /// base pointer as a local; this overload supplies arg.1
        /// (the field-level lift contract) via a temporary copy.
        /// </summary>
        private static void EmitLiftList(
            ILGenerator il, CtListType list, int offset, TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            // Field-level lifts run inside static Lift{Name} methods
            // where arg.0 is the MemoryInstance and arg.1 is the
            // ptr. Stash both as locals and delegate to the
            // base-pointer overload.
            var memoryLocal = il.DeclareLocal(typeof(MemoryInstance));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stloc, memoryLocal);
            var basePtr = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, basePtr);
            EmitLiftListFromBase(il, list, memoryLocal, basePtr, offset, registry, liftMethods);
        }

        /// <summary>
        /// Lift a <c>list&lt;T&gt;</c> reading its (ptr, count) pair
        /// from <c>(memory, basePtr+baseOffset)</c>. Allocates a CLR
        /// <c>T[]</c>, walks <c>count</c> elements (each at
        /// <c>ptr + i * elemSize</c>), and leaves the array on the
        /// stack. Used by both the field-level lift (where basePtr
        /// is a copy of arg.1, baseOffset is the field's offset) and
        /// the direct-return lift (where basePtr is the retArea
        /// pointer, baseOffset is 0).
        /// </summary>
        public static void EmitLiftListFromBase(
            ILGenerator il, CtListType list, LocalBuilder memoryLocal,
            LocalBuilder basePtr, int baseOffset,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            // 1. Read list ptr + count via the memory local (works
            //    for both static Lift methods where arg.0 is memory
            //    and instance wrappers where memory comes from a
            //    field via a separate local).
            var ptr = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, memoryLocal);
            EmitBaseOffsetPush(il, basePtr, baseOffset);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
            il.Emit(OpCodes.Stloc, ptr);

            var count = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldloc, memoryLocal);
            EmitBaseOffsetPush(il, basePtr, baseOffset + 4);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
            il.Emit(OpCodes.Stloc, count);

            // 2. Allocate T[count] + stash to a local for the loop.
            var elemClr = WitTypeEmit.MapClrType(list.Element, registry,
                "list element");
            var arr = il.DeclareLocal(elemClr.MakeArrayType());
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Newarr, elemClr);
            il.Emit(OpCodes.Stloc, arr);

            // 3. for (i = 0; i < count; i++) arr[i] = lift(ptr + i*size)
            var i = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, i);

            var loopHead = il.DefineLabel();
            var loopCond = il.DefineLabel();
            il.Emit(OpCodes.Br, loopCond);
            il.MarkLabel(loopHead);

            il.Emit(OpCodes.Ldloc, arr);
            il.Emit(OpCodes.Ldloc, i);
            EmitLiftElementAt(il, list.Element, memoryLocal, ptr, i, registry, liftMethods);
            EmitStelem(il, elemClr);

            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, i);

            il.MarkLabel(loopCond);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Blt, loopHead);

            il.Emit(OpCodes.Ldloc, arr);
        }

        private static void EmitBaseOffsetPush(
            ILGenerator il, LocalBuilder basePtr, int offset)
        {
            il.Emit(OpCodes.Ldloc, basePtr);
            if (offset != 0)
            {
                il.Emit(OpCodes.Ldc_I4, offset);
                il.Emit(OpCodes.Add);
            }
        }

        /// <summary>
        /// Lift a single list element at <c>(memory, listPtr + i * elemSize)</c>.
        /// Walks the same type-tree as <see cref="EmitLiftField"/>
        /// but parameterizes the pointer over a runtime local pair
        /// (<paramref name="listPtr"/> + <paramref name="indexLocal"/>)
        /// rather than the static arg-slot-1 / offset pair the
        /// field-level lift uses.
        /// </summary>
        private static void EmitLiftElementAt(
            ILGenerator il, CtValType elemType, LocalBuilder memoryLocal,
            LocalBuilder listPtr, LocalBuilder indexLocal,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<string, MethodBuilder> liftMethods)
        {
            var deref = CanonicalAbi.Deref(elemType);
            int elemSize = CanonicalAbi.SizeOf(deref);

            // Compute elemPtr = listPtr + index * elemSize → leave on stack
            // each time we need to read from memory. We don't stash a
            // single elemPtr local because primitives push memory
            // then ptr+offset incrementally; instead build it inline.
            switch (deref)
            {
                case CtPrimType prim when prim.Kind is CtPrim.Bool or CtPrim.S8 or CtPrim.U8:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
                    if (prim.Kind == CtPrim.S8)
                        il.Emit(OpCodes.Conv_I1);
                    return;
                case CtPrimType prim when prim.Kind is CtPrim.S16 or CtPrim.U16 or CtPrim.Char:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI16LE);
                    if (prim.Kind == CtPrim.U16 || prim.Kind == CtPrim.Char)
                        il.Emit(OpCodes.Conv_U2);
                    return;
                case CtPrimType prim when prim.Kind is CtPrim.S32 or CtPrim.U32:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                case CtPrimType prim when prim.Kind is CtPrim.S64 or CtPrim.U64:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI64LE);
                    return;
                case CtPrimType prim when prim.Kind == CtPrim.F32:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadF32LE);
                    return;
                case CtPrimType prim when prim.Kind == CtPrim.F64:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadF64LE);
                    return;
                case CtPrimType prim when prim.Kind == CtPrim.String:
                    // LiftUtf8(memory, ReadI32LE(memory, elemPtr+0), ReadI32LE(memory, elemPtr+4))
                    il.Emit(OpCodes.Ldloc, memoryLocal); // memory for LiftUtf8
                    // ptr
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    // len
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Ldc_I4_4);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    il.Emit(OpCodes.Call, StringCoding_LiftUtf8);
                    return;
                case CtRecordType rec:
                    if (!liftMethods.TryGetValue(rec.Name, out var recLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for record '{rec.Name}'.");
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, recLift);
                    return;
                case CtVariantType variant:
                    if (!liftMethods.TryGetValue(variant.Name, out var varLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for variant '{variant.Name}'.");
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, varLift);
                    return;
                default:
                    throw new NotSupportedException(
                        $"Lift of list element type {deref.GetType().Name} not yet supported.");
            }
        }

        private static void EmitElementPtr(
            ILGenerator il, LocalBuilder listPtr, LocalBuilder indexLocal, int elemSize)
        {
            il.Emit(OpCodes.Ldloc, listPtr);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4, elemSize);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
        }

        private static void EmitStelem(ILGenerator il, Type elemType)
        {
            if (elemType == typeof(int) || elemType == typeof(uint))
                il.Emit(OpCodes.Stelem_I4);
            else if (elemType == typeof(long) || elemType == typeof(ulong))
                il.Emit(OpCodes.Stelem_I8);
            else if (elemType == typeof(float))
                il.Emit(OpCodes.Stelem_R4);
            else if (elemType == typeof(double))
                il.Emit(OpCodes.Stelem_R8);
            else if (elemType == typeof(byte) || elemType == typeof(sbyte))
                il.Emit(OpCodes.Stelem_I1);
            else if (elemType == typeof(short) || elemType == typeof(ushort))
                il.Emit(OpCodes.Stelem_I2);
            else
                il.Emit(OpCodes.Stelem, elemType);   // ref / struct elements
        }

        private static void EmitLiftPrimitive(ILGenerator il, CtPrimType prim, int offset)
        {
            switch (prim.Kind)
            {
                case CtPrim.Bool:
                case CtPrim.S8:
                case CtPrim.U8:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
                    if (prim.Kind == CtPrim.S8)
                        il.Emit(OpCodes.Conv_I1);   // U8 → S8 narrowing for sign-correctness
                    // Bool / U8 / S8 leave CLR primitive on stack.
                    return;
                case CtPrim.S16:
                case CtPrim.U16:
                case CtPrim.Char:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI16LE);
                    if (prim.Kind == CtPrim.U16 || prim.Kind == CtPrim.Char)
                        il.Emit(OpCodes.Conv_U2);
                    return;
                case CtPrim.S32:
                case CtPrim.U32:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                case CtPrim.S64:
                case CtPrim.U64:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI64LE);
                    return;
                case CtPrim.F32:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadF32LE);
                    return;
                case CtPrim.F64:
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadF64LE);
                    return;
                case CtPrim.String:
                    // Strings are stored as (ptr, len) — two i32s at
                    // 4-byte alignment. Read both, then decode UTF-8
                    // via StringCoding.LiftUtf8(memory, ptr, len).
                    // The string body's lifetime is managed by the
                    // owning record/variant's cabi_post_<name> call
                    // at the export-method level (NeedsPostReturn=true
                    // when the return transitively contains strings).
                    il.Emit(OpCodes.Ldarg_0);           // memory (for LiftUtf8 — pushed first)
                    il.Emit(OpCodes.Ldarg_0);           // memory (for ReadI32LE ptr)
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE); // → ptr on stack
                    il.Emit(OpCodes.Ldarg_0);           // memory (for ReadI32LE len)
                    EmitOffsetPush(il, offset + 4);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE); // → len on stack
                    il.Emit(OpCodes.Call, StringCoding_LiftUtf8);   // (memory, ptr, len) → string
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
