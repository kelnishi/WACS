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
        public static System.Collections.Generic.Dictionary<CtValType, MethodBuilder> EmitLifts(
            TypeBuilder harnessType, CtWorldType world, TypeRegistry registry)
        {
            var liftMethods = new System.Collections.Generic.Dictionary<CtValType, MethodBuilder>();

            // Pass 1: define method signatures so cross-calls resolve.
            // Walks the registry directly so interface-declared types
            // get lifts alongside world-level types. Method names use
            // the TypeBuilder's short name (which incorporates the
            // interface namespace dot-segment) with dots collapsed,
            // ensuring uniqueness even when two interfaces declare a
            // same-named type.
            foreach (var (rec, tb) in registry.Records)
            {
                var clr = WitTypeEmit.MapClrType(rec, registry, $"lift signature for record");
                var mb = harnessType.DefineMethod(
                    UniqueLiftMethodName(tb),
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                    clr,
                    new[] { typeof(MemoryInstance), typeof(int) });
                mb.DefineParameter(1, ParameterAttributes.None, "memory");
                mb.DefineParameter(2, ParameterAttributes.None, "ptr");
                liftMethods[rec] = mb;
            }
            foreach (var (variant, tb) in registry.Variants)
            {
                var clr = WitTypeEmit.MapClrType(variant, registry, $"lift signature for variant");
                var mb = harnessType.DefineMethod(
                    UniqueLiftMethodName(tb),
                    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
                    clr,
                    new[] { typeof(MemoryInstance), typeof(int) });
                mb.DefineParameter(1, ParameterAttributes.None, "memory");
                mb.DefineParameter(2, ParameterAttributes.None, "ptr");
                liftMethods[variant] = mb;
            }

            // Pass 2: fill bodies.
            foreach (var (rec, mb) in liftMethods)
            {
                var il = mb.GetILGenerator();
                if (rec is CtRecordType r)
                    EmitRecordLift(il, r, registry, liftMethods);
                else if (rec is CtVariantType v)
                    EmitVariantLift(il, v, registry, liftMethods);
                il.Emit(OpCodes.Ret);
            }

            return liftMethods;
        }

        private static string UniqueLiftMethodName(TypeBuilder tb)
        {
            // tb.Name is the type's short name (without namespace);
            // for interface-typed records that already lives in a
            // sub-namespace (e.g. "Error" inside ns
            // "...Generated.WasiIoStreams"), tb.FullName carries the
            // interface segment. Use FullName minus the assembly's
            // common prefix to avoid collisions.
            return "Lift_" + tb.FullName!.Replace('.', '_');
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
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var deref = CanonicalAbi.Deref(fieldType);
            switch (deref)
            {
                case CtPrimType prim:
                    EmitLiftPrimitive(il, prim, offset);
                    return;

                case CtRecordType rec:
                    if (!liftMethods.TryGetValue(rec, out var recLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for record '{rec.Name}'.");
                    il.Emit(OpCodes.Ldarg_0);
                    EmitOffsetPush(il, offset);
                    il.Emit(OpCodes.Call, recLift);
                    return;

                case CtVariantType variant:
                    if (!liftMethods.TryGetValue(variant, out var varLift))
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

                case CtOptionType opt:
                    EmitLiftOption(il, opt, offset, registry, liftMethods);
                    return;

                case CtResultType res:
                    EmitLiftResult(il, res, offset, registry, liftMethods);
                    return;

                case CtTupleType tup:
                    EmitLiftTuple(il, tup, offset, registry, liftMethods);
                    return;

                default:
                    throw new NotSupportedException(
                        $"LiftEmit v0.2 does not support {deref.GetType().Name}.");
            }
        }

        /// <summary>
        /// Emit IL lifting an <c>option&lt;T&gt;</c> at <c>(arg.1 +
        /// offset)</c>. Read disc byte; on 0 push none (null for
        /// reference T, default <c>Nullable&lt;T&gt;</c> for value
        /// T); on 1 lift T at the payload offset and wrap if T is
        /// a value type. Leaves the option-typed value on the stack.
        /// </summary>
        private static void EmitLiftOption(
            ILGenerator il, CtOptionType opt, int offset, TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var innerClr = WitTypeEmit.MapClrType(opt.Inner, registry, "option inner");
            int payloadOffset = offset + CanonicalAbi.AlignUp(1, CanonicalAbi.AlignOf(opt.Inner));

            var disc = il.DeclareLocal(typeof(byte));
            il.Emit(OpCodes.Ldarg_0);
            EmitOffsetPush(il, offset);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
            il.Emit(OpCodes.Stloc, disc);

            var noneLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, disc);
            il.Emit(OpCodes.Brfalse, noneLabel);

            // some(T)
            EmitLiftField(il, opt.Inner, payloadOffset, registry, liftMethods);
            if (innerClr.IsValueType)
            {
                // wrap in Nullable<T>
                var nullableT = typeof(System.Nullable<>).MakeGenericType(innerClr);
                var ctor = nullableT.GetConstructor(new[] { innerClr })!;
                il.Emit(OpCodes.Newobj, ctor);
            }
            il.Emit(OpCodes.Br, endLabel);

            // none
            il.MarkLabel(noneLabel);
            if (innerClr.IsValueType)
            {
                var nullableT = typeof(System.Nullable<>).MakeGenericType(innerClr);
                var defaultLocal = il.DeclareLocal(nullableT);
                il.Emit(OpCodes.Ldloca, defaultLocal);
                il.Emit(OpCodes.Initobj, nullableT);
                il.Emit(OpCodes.Ldloc, defaultLocal);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Emit IL lifting a <c>result&lt;TOk, TErr&gt;</c> at
        /// <c>(arg.1 + offset)</c>. Read disc, branch on 0 (Ok) or
        /// 1 (Err); for present sides, lift the payload at the
        /// aligned payload offset and call the matching factory; for
        /// elided sides, push <c>default(ValueTuple)</c>. Leaves a
        /// <c>WitResult&lt;TOk, TErr&gt;</c> on the stack.
        /// </summary>
        private static void EmitLiftResult(
            ILGenerator il, CtResultType res, int offset, TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var okClr = res.Ok == null
                ? typeof(System.ValueTuple)
                : WitTypeEmit.MapClrType(res.Ok, registry, "result ok");
            var errClr = res.Err == null
                ? typeof(System.ValueTuple)
                : WitTypeEmit.MapClrType(res.Err, registry, "result err");
            var resultType = typeof(Wacs.ComponentModel.Harness.WitResult<,>)
                .MakeGenericType(okClr, errClr);
            var okFactory = resultType.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
            var errFactory = resultType.GetMethod("Err", BindingFlags.Public | BindingFlags.Static)!;

            int okAlign = res.Ok != null ? CanonicalAbi.AlignOf(res.Ok) : 1;
            int errAlign = res.Err != null ? CanonicalAbi.AlignOf(res.Err) : 1;
            int payloadOffset = offset + CanonicalAbi.AlignUp(1, Math.Max(okAlign, errAlign));

            var disc = il.DeclareLocal(typeof(byte));
            il.Emit(OpCodes.Ldarg_0);
            EmitOffsetPush(il, offset);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
            il.Emit(OpCodes.Stloc, disc);

            var errLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, disc);
            il.Emit(OpCodes.Brtrue, errLabel);

            // ok branch (disc == 0)
            if (res.Ok != null)
                EmitLiftField(il, res.Ok, payloadOffset, registry, liftMethods);
            else
                EmitDefaultValueTuple(il);
            il.Emit(OpCodes.Call, okFactory);
            il.Emit(OpCodes.Br, endLabel);

            // err branch (disc == 1)
            il.MarkLabel(errLabel);
            if (res.Err != null)
                EmitLiftField(il, res.Err, payloadOffset, registry, liftMethods);
            else
                EmitDefaultValueTuple(il);
            il.Emit(OpCodes.Call, errFactory);

            il.MarkLabel(endLabel);
        }

        /// <summary>
        /// Emit IL lifting a <c>tuple&lt;T1, T2, …&gt;</c> at
        /// <c>(arg.1 + offset)</c>. Walks the per-element offsets,
        /// lifts each element to the stack in declaration order, then
        /// constructs the closed <c>ValueTuple&lt;…&gt;</c> via its
        /// positional ctor. Leaves the tuple-typed value on the stack.
        /// </summary>
        private static void EmitLiftTuple(
            ILGenerator il, CtTupleType tup, int offset, TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            int[] elemOffsets = CanonicalAbi.TupleElementOffsets(tup);
            for (int i = 0; i < tup.Elements.Count; i++)
                EmitLiftField(il, tup.Elements[i], offset + elemOffsets[i], registry, liftMethods);

            var tupleClr = WitTypeEmit.MapClrType(tup, registry, "tuple");
            var elemClrs = new Type[tup.Elements.Count];
            for (int i = 0; i < tup.Elements.Count; i++)
                elemClrs[i] = WitTypeEmit.MapClrType(tup.Elements[i], registry, $"tuple element {i}");
            var ctor = tupleClr.GetConstructor(elemClrs)!;
            il.Emit(OpCodes.Newobj, ctor);
        }

        private static void EmitDefaultValueTuple(ILGenerator il)
        {
            var local = il.DeclareLocal(typeof(System.ValueTuple));
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Initobj, typeof(System.ValueTuple));
            il.Emit(OpCodes.Ldloc, local);
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
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var offsets = CanonicalAbi.RecordFieldOffsets(rec);

            // For each field: push the lifted value onto the stack in
            // declaration order. The record's positional ctor takes
            // them in the same order.
            for (int i = 0; i < rec.Fields.Count; i++)
                EmitLiftField(il, rec.Fields[i].Type, offsets[i], registry, liftMethods);

            // Newobj the record class. Constructor token stashed by
            // WitTypeEmit when it emitted the type.
            il.Emit(OpCodes.Newobj, registry.RecordCtors[rec]);
        }

        private static void EmitVariantLift(
            ILGenerator il, CtVariantType variant, TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
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
            var caseCtors = registry.VariantCaseCtors[variant];
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
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
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
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
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
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
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
                    if (!liftMethods.TryGetValue(rec, out var recLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for record '{rec.Name}'.");
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, recLift);
                    return;
                case CtVariantType variant:
                    if (!liftMethods.TryGetValue(variant, out var varLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for variant '{variant.Name}'.");
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Call, varLift);
                    return;
                case CtEnumType en:
                    EmitReadIntegerAtElement(il, memoryLocal, listPtr, indexLocal, elemSize, 0,
                        CanonicalAbi.VariantDiscSize(en.Cases.Count));
                    return;
                case CtFlagsType fl:
                    EmitReadIntegerAtElement(il, memoryLocal, listPtr, indexLocal, elemSize, 0,
                        CanonicalAbi.FlagsByteWidth(fl.Flags.Count));
                    return;
                case CtListType nestedList:
                {
                    // Element occupies 8 bytes (ptr, count). Stash
                    // elemPtr into a local + reuse the from-base
                    // list lifter.
                    var elemPtrLocal = il.DeclareLocal(typeof(int));
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Stloc, elemPtrLocal);
                    EmitLiftListFromBase(il, nestedList, memoryLocal, elemPtrLocal, 0,
                        registry, liftMethods);
                    return;
                }
                case CtOptionType opt:
                case CtResultType:
                case CtTupleType:
                {
                    // For composite shapes inside a list element,
                    // stash the element's start address and delegate
                    // to the generic "lift from base" walker that
                    // handles offset arithmetic.
                    var elemPtrLocal = il.DeclareLocal(typeof(int));
                    EmitElementPtr(il, listPtr, indexLocal, elemSize);
                    il.Emit(OpCodes.Stloc, elemPtrLocal);
                    EmitLiftFromBase(il, deref, memoryLocal, elemPtrLocal, 0,
                        registry, liftMethods);
                    return;
                }
                default:
                    throw new NotSupportedException(
                        $"Lift of list element type {deref.GetType().Name} not yet supported.");
            }
        }

        /// <summary>
        /// Read an unsigned integer of width 1/2/4 at
        /// <c>(memoryLocal, listPtr + i*elemSize + offset)</c>.
        /// Element-context counterpart of <see cref="EmitReadIntegerWidth"/>.
        /// </summary>
        private static void EmitReadIntegerAtElement(
            ILGenerator il, LocalBuilder memoryLocal,
            LocalBuilder listPtr, LocalBuilder indexLocal,
            int elemSize, int offsetWithinElem, int width)
        {
            switch (width)
            {
                case 1:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtrOffset(il, listPtr, indexLocal, elemSize, offsetWithinElem);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
                    return;
                case 2:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtrOffset(il, listPtr, indexLocal, elemSize, offsetWithinElem);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI16LE);
                    il.Emit(OpCodes.Conv_U2);
                    return;
                case 4:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitElementPtrOffset(il, listPtr, indexLocal, elemSize, offsetWithinElem);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                default:
                    throw new NotSupportedException($"Unsupported integer width {width}.");
            }
        }

        private static void EmitElementPtrOffset(
            ILGenerator il, LocalBuilder listPtr, LocalBuilder indexLocal,
            int elemSize, int offsetWithinElem)
        {
            EmitElementPtr(il, listPtr, indexLocal, elemSize);
            if (offsetWithinElem != 0)
            {
                il.Emit(OpCodes.Ldc_I4, offsetWithinElem);
                il.Emit(OpCodes.Add);
            }
        }

        /// <summary>
        /// Lift a value at <c>(memoryLocal, basePtr + offset)</c>.
        /// Mirrors <see cref="EmitLiftField"/> but takes the memory
        /// + ptr via locals rather than the fixed arg-slot contract.
        /// Used by list-element lifts when the element type is a
        /// composite (option / result / tuple) whose payload offsets
        /// chain off the element's start address.
        /// </summary>
        public static void EmitLiftFromBase(
            ILGenerator il, CtValType valueType,
            LocalBuilder memoryLocal, LocalBuilder basePtr, int offset,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var d = CanonicalAbi.Deref(valueType);
            switch (d)
            {
                case CtPrimType prim:
                    EmitLiftPrimitiveFromBase(il, prim, memoryLocal, basePtr, offset);
                    return;
                case CtRecordType rec:
                    if (!liftMethods.TryGetValue(rec, out var recLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for record '{rec.Name}'.");
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, recLift);
                    return;
                case CtVariantType variant:
                    if (!liftMethods.TryGetValue(variant, out var varLift))
                        throw new InvalidOperationException(
                            $"No Lift method registered for variant '{variant.Name}'.");
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, varLift);
                    return;
                case CtEnumType en:
                    EmitReadIntegerAtBase(il, memoryLocal, basePtr, offset,
                        CanonicalAbi.VariantDiscSize(en.Cases.Count));
                    return;
                case CtFlagsType fl:
                    EmitReadIntegerAtBase(il, memoryLocal, basePtr, offset,
                        CanonicalAbi.FlagsByteWidth(fl.Flags.Count));
                    return;
                case CtListType list:
                    EmitLiftListFromBase(il, list, memoryLocal, basePtr, offset, registry, liftMethods);
                    return;
                case CtOptionType opt:
                    EmitLiftOptionFromBase(il, opt, memoryLocal, basePtr, offset, registry, liftMethods);
                    return;
                case CtResultType res:
                    EmitLiftResultFromBase(il, res, memoryLocal, basePtr, offset, registry, liftMethods);
                    return;
                case CtTupleType tup:
                    EmitLiftTupleFromBase(il, tup, memoryLocal, basePtr, offset, registry, liftMethods);
                    return;
                default:
                    throw new NotSupportedException(
                        $"EmitLiftFromBase doesn't support {d.GetType().Name}.");
            }
        }

        private static void EmitLiftPrimitiveFromBase(
            ILGenerator il, CtPrimType prim, LocalBuilder memoryLocal,
            LocalBuilder basePtr, int offset)
        {
            switch (prim.Kind)
            {
                case CtPrim.Bool:
                case CtPrim.S8:
                case CtPrim.U8:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
                    if (prim.Kind == CtPrim.S8) il.Emit(OpCodes.Conv_I1);
                    return;
                case CtPrim.S16:
                case CtPrim.U16:
                case CtPrim.Char:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI16LE);
                    if (prim.Kind == CtPrim.U16 || prim.Kind == CtPrim.Char)
                        il.Emit(OpCodes.Conv_U2);
                    return;
                case CtPrim.S32:
                case CtPrim.U32:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                case CtPrim.S64:
                case CtPrim.U64:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI64LE);
                    return;
                case CtPrim.F32:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadF32LE);
                    return;
                case CtPrim.F64:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadF64LE);
                    return;
                case CtPrim.String:
                    il.Emit(OpCodes.Ldloc, memoryLocal);   // memory for LiftUtf8
                    il.Emit(OpCodes.Ldloc, memoryLocal);   // ptr load
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    il.Emit(OpCodes.Ldloc, memoryLocal);   // len load
                    EmitBaseOffsetPush(il, basePtr, offset + 4);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    il.Emit(OpCodes.Call, StringCoding_LiftUtf8);
                    return;
                default:
                    throw new NotSupportedException(
                        $"EmitLiftPrimitiveFromBase doesn't support {prim.Kind}.");
            }
        }

        private static void EmitReadIntegerAtBase(
            ILGenerator il, LocalBuilder memoryLocal, LocalBuilder basePtr,
            int offset, int width)
        {
            switch (width)
            {
                case 1:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
                    return;
                case 2:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI16LE);
                    il.Emit(OpCodes.Conv_U2);
                    return;
                case 4:
                    il.Emit(OpCodes.Ldloc, memoryLocal);
                    EmitBaseOffsetPush(il, basePtr, offset);
                    il.Emit(OpCodes.Call, MemoryHelpers_ReadI32LE);
                    return;
                default:
                    throw new NotSupportedException($"Unsupported integer width {width}.");
            }
        }

        private static void EmitLiftOptionFromBase(
            ILGenerator il, CtOptionType opt,
            LocalBuilder memoryLocal, LocalBuilder basePtr, int offset,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var innerClr = WitTypeEmit.MapClrType(opt.Inner, registry, "option inner");
            int payloadOffset = offset + CanonicalAbi.AlignUp(1, CanonicalAbi.AlignOf(opt.Inner));

            var disc = il.DeclareLocal(typeof(byte));
            il.Emit(OpCodes.Ldloc, memoryLocal);
            EmitBaseOffsetPush(il, basePtr, offset);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
            il.Emit(OpCodes.Stloc, disc);

            var noneLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, disc);
            il.Emit(OpCodes.Brfalse, noneLabel);

            // Some
            EmitLiftFromBase(il, opt.Inner, memoryLocal, basePtr, payloadOffset, registry, liftMethods);
            if (innerClr.IsValueType)
            {
                var nullableT = typeof(System.Nullable<>).MakeGenericType(innerClr);
                var ctor = nullableT.GetConstructor(new[] { innerClr })!;
                il.Emit(OpCodes.Newobj, ctor);
            }
            il.Emit(OpCodes.Br, endLabel);

            // None
            il.MarkLabel(noneLabel);
            if (innerClr.IsValueType)
            {
                var nullableT = typeof(System.Nullable<>).MakeGenericType(innerClr);
                var defaultLocal = il.DeclareLocal(nullableT);
                il.Emit(OpCodes.Ldloca, defaultLocal);
                il.Emit(OpCodes.Initobj, nullableT);
                il.Emit(OpCodes.Ldloc, defaultLocal);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.MarkLabel(endLabel);
        }

        private static void EmitLiftResultFromBase(
            ILGenerator il, CtResultType res,
            LocalBuilder memoryLocal, LocalBuilder basePtr, int offset,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            var okClr = res.Ok == null
                ? typeof(System.ValueTuple)
                : WitTypeEmit.MapClrType(res.Ok, registry, "result ok");
            var errClr = res.Err == null
                ? typeof(System.ValueTuple)
                : WitTypeEmit.MapClrType(res.Err, registry, "result err");
            var resultType = typeof(Wacs.ComponentModel.Harness.WitResult<,>)
                .MakeGenericType(okClr, errClr);
            var okFactory = resultType.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
            var errFactory = resultType.GetMethod("Err", BindingFlags.Public | BindingFlags.Static)!;

            int okAlign = res.Ok != null ? CanonicalAbi.AlignOf(res.Ok) : 1;
            int errAlign = res.Err != null ? CanonicalAbi.AlignOf(res.Err) : 1;
            int payloadOffset = offset + CanonicalAbi.AlignUp(1, Math.Max(okAlign, errAlign));

            var disc = il.DeclareLocal(typeof(byte));
            il.Emit(OpCodes.Ldloc, memoryLocal);
            EmitBaseOffsetPush(il, basePtr, offset);
            il.Emit(OpCodes.Call, MemoryHelpers_ReadU8);
            il.Emit(OpCodes.Stloc, disc);

            var errLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, disc);
            il.Emit(OpCodes.Brtrue, errLabel);

            if (res.Ok != null)
                EmitLiftFromBase(il, res.Ok, memoryLocal, basePtr, payloadOffset, registry, liftMethods);
            else
                EmitDefaultValueTuple(il);
            il.Emit(OpCodes.Call, okFactory);
            il.Emit(OpCodes.Br, endLabel);

            il.MarkLabel(errLabel);
            if (res.Err != null)
                EmitLiftFromBase(il, res.Err, memoryLocal, basePtr, payloadOffset, registry, liftMethods);
            else
                EmitDefaultValueTuple(il);
            il.Emit(OpCodes.Call, errFactory);

            il.MarkLabel(endLabel);
        }

        private static void EmitLiftTupleFromBase(
            ILGenerator il, CtTupleType tup,
            LocalBuilder memoryLocal, LocalBuilder basePtr, int offset,
            TypeRegistry registry,
            System.Collections.Generic.Dictionary<CtValType, MethodBuilder> liftMethods)
        {
            int[] elemOffsets = CanonicalAbi.TupleElementOffsets(tup);
            for (int i = 0; i < tup.Elements.Count; i++)
                EmitLiftFromBase(il, tup.Elements[i], memoryLocal, basePtr,
                    offset + elemOffsets[i], registry, liftMethods);

            var tupleClr = WitTypeEmit.MapClrType(tup, registry, "tuple");
            var elemClrs = new Type[tup.Elements.Count];
            for (int i = 0; i < tup.Elements.Count; i++)
                elemClrs[i] = WitTypeEmit.MapClrType(tup.Elements[i], registry, $"tuple element {i}");
            var ctor = tupleClr.GetConstructor(elemClrs)!;
            il.Emit(OpCodes.Newobj, ctor);
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
