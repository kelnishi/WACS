// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using WacsCoreModule = Wacs.Core.Module;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Interpreter-side companion to the AOT transpiler — wraps a
    /// parsed <see cref="ComponentModule"/> + an instantiated
    /// <see cref="WasmRuntime"/> so callers can invoke component-
    /// level exports through the canonical ABI without ever
    /// emitting IL.
    ///
    /// <para>Parallel to <see cref="ModuleInstance"/> on the core
    /// side: holds the per-component state (the runtime backing
    /// the embedded core module(s), the canonical-ABI lift/lower
    /// machinery, eventually a resource-handle table). Phase 1c
    /// v0 scope: single-core-module components, primitive +
    /// string returns through <see cref="Invoke"/>. Aggregate
    /// returns + the typed binding-layer surfaces
    /// (<c>BindComponentInterface&lt;T&gt;</c> / etc.) layer on
    /// top in follow-ups.</para>
    ///
    /// <para><b>Why an interpreter path at all when the
    /// transpiler exists?</b> Two reasons. First, fast iteration
    /// during component development — no .dll roundtrip per
    /// fixture change. Second, dual-engine equivalence: running
    /// the same component through the interpreter AND the
    /// transpiler is the cheapest cross-check on the canonical-
    /// ABI implementation. Bug in either side surfaces as a
    /// disagreement.</para>
    /// </summary>
    public sealed class ComponentInstance
    {
        private readonly ComponentModule _component;
        private readonly WasmRuntime _runtime;
        /// <summary>Per-Invoke string encoding (set from the
        /// matching canon-lift's options). Threaded as field
        /// rather than method parameter because string lifts fan
        /// out across many helpers (return, list-of-string,
        /// option-string, result-string, variant-string-payload).
        /// Reset on each Invoke entry.</summary>
        private CanonOption.Kind _stringEncoding =
            CanonOption.Kind.StringUtf8;
        private readonly ModuleInstance _coreInstance;
        private MemoryInstance? _memory;
        private Wacs.Core.Runtime.Delegates.GenericFuncs? _cabiRealloc;

        private ComponentInstance(
            ComponentModule component,
            WasmRuntime runtime,
            ModuleInstance coreInstance)
        {
            _component = component;
            _runtime = runtime;
            _coreInstance = coreInstance;
            _runtime.TryGetExportedMemory("memory", out _memory!);
        }

        /// <summary>
        /// Parse + instantiate a component binary in one pass.
        /// Single-core-module components only for v0 — multi-
        /// module composition lands when Phase 1c grows up to
        /// match the transpiler's coverage.
        /// </summary>
        public static ComponentInstance Instantiate(byte[] componentBytes)
        {
            using var ms = new MemoryStream(componentBytes);
            return Instantiate(ms);
        }

        public static ComponentInstance Instantiate(Stream componentStream)
        {
            var component = ComponentBinaryParser.Parse(componentStream);
            var coreBinaries = component.CoreModuleBinaries.ToList();
            if (coreBinaries.Count != 1)
                throw new InvalidOperationException(
                    "ComponentInstance.Instantiate requires exactly one "
                    + "embedded core module; got "
                    + coreBinaries.Count + ".");

            var runtime = new WasmRuntime();
            using var coreMs = new MemoryStream(coreBinaries[0]);
            var coreModule = BinaryModuleParser.ParseWasm(coreMs);
            var coreInstance = runtime.InstantiateModule(coreModule);

            return new ComponentInstance(component, runtime, coreInstance);
        }

        /// <summary>The parsed component this instance backs —
        /// useful for callers that want to inspect declared
        /// exports / types before invoking.</summary>
        public ComponentModule Component => _component;

        /// <summary>
        /// Invoke a component-level export by name with the
        /// supplied user args. Resolves the export to its
        /// underlying canon lift, looks up the matching core
        /// function (the convention wit-component uses: core
        /// export name == component export name), lowers any
        /// aggregate args, calls through, and lifts the result
        /// back into a managed value.
        ///
        /// <para>v0 lift coverage: primitives (i32/i64/f32/f64
        /// pass-through, narrow ints + bool), string returns
        /// (StringMarshal.LiftUtf8 from the return-area). Other
        /// aggregate returns route through <see cref="WasmRuntime"/>
        /// directly — the caller gets the raw retArea pointer
        /// and can lift further via the same canonical-ABI
        /// helpers the transpiler emits IL against.</para>
        /// </summary>
        public object? Invoke(string exportName, params object?[] args)
        {
            var componentExport = _component.Exports
                .FirstOrDefault(e => e.Name == exportName
                    && e.Sort == ComponentSort.Func)
                ?? throw new ArgumentException(
                    $"No component-level Func export named '{exportName}'.",
                    nameof(exportName));

            if (!_component.ComponentFuncToCanon.TryGetValue(
                    componentExport.Index, out var lift))
                throw new InvalidOperationException(
                    "Component export '" + exportName + "' has no "
                    + "matching canon lift; component may be malformed.");

            if (lift.TypeIdx >= _component.Types.Count
                || !(_component.Types[(int)lift.TypeIdx]
                        is ComponentFuncType fn))
                throw new InvalidOperationException(
                    "Canon lift for '" + exportName + "' references a "
                    + "non-function type; component may be malformed.");

            // Map the component-level export name to the core
            // export. wit-component preserves the name verbatim
            // for world-level exports, so name-based lookup works
            // for all our fixtures.
            if (!_runtime.TryGetExportedFunction(exportName, out var coreAddr))
                throw new InvalidOperationException(
                    "Core module has no export matching component-level "
                    + "export '" + exportName + "'.");

            var coreFunc = _runtime.GetFunction(coreAddr);
            var coreFuncType = coreFunc.Type;

            // The runtime's untyped invoker takes `object[]` and
            // returns `Value[]`. Boxing is fine here — Phase 1c is
            // about correctness, not throughput. (The transpiler
            // path stays the fast lane; the interpreter is the
            // checking lane.)
            var invoker = _runtime.CreateInvoker(coreAddr, new InvokerOptions());
            var coreArgs = LowerArgs(fn, args);
            var coreResults = invoker(coreArgs);

            _stringEncoding = ResolveStringEncoding(lift.Options);
            return LiftResult(fn, coreFuncType, coreResults);
        }

        /// <summary>Pick the export's string encoding from its
        /// canon-lift options. Mirror of the transpiler's
        /// ResolveStringEncoding — defaults to UTF-8 when no
        /// option present.</summary>
        private static CanonOption.Kind ResolveStringEncoding(
            IReadOnlyList<CanonOption> options)
        {
            foreach (var opt in options)
            {
                switch (opt.OptionKind)
                {
                    case CanonOption.Kind.StringUtf8:
                    case CanonOption.Kind.StringUtf16:
                    case CanonOption.Kind.StringLatin1OrUtf16:
                        return opt.OptionKind;
                }
            }
            return CanonOption.Kind.StringUtf8;
        }

        // ---- Lower (managed → core wire) -----------------------------

        private object[] LowerArgs(ComponentFuncType fn, object?[] userArgs)
        {
            if (fn.Params.Count != userArgs.Length)
                throw new ArgumentException(
                    $"Expected {fn.Params.Count} args, got "
                    + $"{userArgs.Length}.");

            // Primitive params pass through; string + list<prim>
            // params route through cabi_realloc + a memcpy into
            // exported memory, then push (ptr, count) — same
            // shape the transpiler's EmitPrecomputeBufferParam
            // helpers emit IL for.
            var lowered = new List<object>(fn.Params.Count);
            for (int i = 0; i < fn.Params.Count; i++)
            {
                var pt = fn.Params[i].Type;
                if (pt.IsPrimitive)
                {
                    if (pt.Prim == ComponentPrim.String)
                    {
                        var (ptr, len) = LowerStringParam((string)userArgs[i]!);
                        lowered.Add(ptr);
                        lowered.Add(len);
                    }
                    else
                    {
                        lowered.Add(LowerPrimitive(pt.Prim, userArgs[i])!);
                    }
                }
                else if (!pt.IsPrimitive
                    && pt.TypeIdx < _component.Types.Count
                    && _component.Types[(int)pt.TypeIdx]
                        is ComponentListType list
                    && list.Element.IsPrimitive
                    && list.Element.Prim != ComponentPrim.String)
                {
                    var (ptr, count) = LowerListOfPrimParam(
                        userArgs[i], list.Element.Prim);
                    lowered.Add(ptr);
                    lowered.Add(count);
                }
                else if (!pt.IsPrimitive
                    && pt.TypeIdx < _component.Types.Count
                    && (_component.Types[(int)pt.TypeIdx]
                            is ComponentOwnType
                        || _component.Types[(int)pt.TypeIdx]
                            is ComponentBorrowType))
                {
                    // own<R> / borrow<R> param — accept the raw
                    // i32 handle from the user. Without dynamic-
                    // type emission for the resource class, this
                    // is the simplest binding shape.
                    lowered.Add(Convert.ToInt32(userArgs[i]));
                }
                else
                {
                    throw new NotSupportedException(
                        "Lower-side marshaling for this aggregate "
                        + "param shape is a follow-up. v0 covers "
                        + "primitive + string + list<prim> + "
                        + "own/borrow<R> (as int handles) params.");
                }
            }
            return lowered.ToArray();
        }

        private (int ptr, int len) LowerStringParam(string value)
        {
            if (_memory == null)
                throw new InvalidOperationException(
                    "String param lowering requires Module.Memory.");
            var bytes = StringMarshal.EncodeUtf8(value);
            var ptr = CabiRealloc(0, 0, 1, bytes.Length);
            StringMarshal.CopyToGuest(bytes, _memory.Data, ptr);
            return (ptr, bytes.Length);
        }

        private (int ptr, int count) LowerListOfPrimParam(
            object? userArg, ComponentPrim elemPrim)
        {
            if (_memory == null)
                throw new InvalidOperationException(
                    "List param lowering requires Module.Memory.");
            if (userArg is not Array arr)
                throw new ArgumentException(
                    "list<" + elemPrim + "> param requires a "
                    + "compatible array argument.");
            var elemSize = PrimByteSize(elemPrim);
            var count = arr.Length;
            var byteLen = count * elemSize;
            var ptr = CabiRealloc(0, 0, elemSize, byteLen);

            // Closed-generic dispatch over ListMarshal.CopyArrayToGuest
            // — same MakeGenericMethod pattern the transpiler IL
            // uses, but at runtime via reflection.
            var elemCs = PrimToCs(elemPrim);
            var copyOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.CopyArrayToGuest))!;
            var copyClosed = copyOpen.MakeGenericMethod(elemCs);
            // Coerce the raw array to the closed element type if
            // the user passed e.g. uint[] for a u32 list — the
            // helper's `T : unmanaged` constraint requires a
            // matching CLR array. For mismatches the user gets
            // a clear conversion error here rather than a
            // confusing reflection failure later.
            if (arr.GetType().GetElementType() != elemCs)
            {
                var typed = Array.CreateInstance(elemCs, count);
                Array.Copy(arr, typed, count);
                arr = typed;
            }
            copyClosed.Invoke(null, new object[] { arr, _memory.Data, ptr });
            return (ptr, count);
        }

        /// <summary>Call the guest's <c>cabi_realloc</c> export
        /// to allocate (or grow / move) a buffer in the
        /// component's exported memory. Cached on first call —
        /// every aggregate-param lowering routes through here.</summary>
        private int CabiRealloc(int oldPtr, int oldLen, int align, int newLen)
        {
            if (_cabiRealloc == null)
            {
                if (!_runtime.TryGetExportedFunction(
                        "cabi_realloc", out var addr))
                    throw new InvalidOperationException(
                        "Component does not export cabi_realloc; "
                        + "aggregate params require it.");
                _cabiRealloc = _runtime.CreateInvoker(
                    addr, new InvokerOptions());
            }
            var results = _cabiRealloc(oldPtr, oldLen, align, newLen);
            return results[0].Data.Int32;
        }

        private static int PrimByteSize(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool => 1,
            ComponentPrim.S8   => 1,
            ComponentPrim.U8   => 1,
            ComponentPrim.S16  => 2,
            ComponentPrim.U16  => 2,
            ComponentPrim.S32  => 4,
            ComponentPrim.U32  => 4,
            ComponentPrim.F32  => 4,
            ComponentPrim.Char => 4,
            ComponentPrim.S64  => 8,
            ComponentPrim.U64  => 8,
            ComponentPrim.F64  => 8,
            _ => throw new NotSupportedException(
                "PrimByteSize for " + p + " is a follow-up."),
        };

        private static object LowerPrimitive(ComponentPrim prim, object? value) =>
            prim switch
            {
                ComponentPrim.Bool => (value is bool b && b) ? (object)1 : 0,
                ComponentPrim.S8   => Convert.ToInt32(value),
                ComponentPrim.U8   => Convert.ToInt32(value),
                ComponentPrim.S16  => Convert.ToInt32(value),
                ComponentPrim.U16  => Convert.ToInt32(value),
                ComponentPrim.S32  => Convert.ToInt32(value),
                ComponentPrim.U32  => (object)unchecked((int)Convert.ToUInt32(value)),
                ComponentPrim.S64  => Convert.ToInt64(value),
                ComponentPrim.U64  => (object)unchecked((long)Convert.ToUInt64(value)),
                ComponentPrim.F32  => Convert.ToSingle(value),
                ComponentPrim.F64  => Convert.ToDouble(value),
                ComponentPrim.Char => Convert.ToInt32(value),
                _ => throw new NotSupportedException(
                    "Lower-side primitive marshaling for " + prim
                    + " is a follow-up."),
            };

        // ---- Lift (core wire → managed) ------------------------------

        private object? LiftResult(
            ComponentFuncType fn, FunctionType coreFuncType,
            Wacs.Core.Runtime.Value[] coreResults)
        {
            if (fn.Results.Count == 0) return null;
            if (fn.Results.Count != 1)
                throw new NotSupportedException(
                    "Multi-result lifts are a follow-up.");
            if (coreResults.Length == 0)
                throw new InvalidOperationException(
                    "Core returned no values for an export with a "
                    + "result type. Component / core signatures are "
                    + "out of sync.");

            var r = fn.Results[0];
            var coreResult = coreResults[0];

            // String is encoded as a primitive at the WIT type
            // level but always lifted via the return-area
            // pointer the core leaves behind — special-case it
            // before the primitive switch.
            if (IsStringRef(r))
                return LiftStringRetArea(coreResult.Data.Int32);

            if (r.IsPrimitive)
                return LiftPrimitiveFromValue(r.Prim, coreResult);

            // Type-ref aggregates. These each share a pattern:
            // core returns the retArea pointer P; the lift reads
            // bytes from memory at P and routes them through the
            // same *Marshal helpers the transpiler emits IL
            // against — so the two engines share a single source
            // of truth for ABI semantics.
            if (TryLiftListOfPrim(r, coreResult.Data.Int32,
                    out var listResult))
                return listResult;
            if (TryLiftListOfString(r, coreResult.Data.Int32,
                    out var listStringResult))
                return listStringResult;
            if (TryLiftOptionOfPrim(r, coreResult.Data.Int32,
                    out var optionResult))
                return optionResult;
            if (TryLiftOptionOfString(r, coreResult.Data.Int32,
                    out var optionStringResult))
                return optionStringResult;
            if (TryLiftTupleOfPrims(r, coreResult.Data.Int32,
                    out var tupleResult))
                return tupleResult;
            if (TryLiftResult(r, coreResult.Data.Int32,
                    out var resultValue))
                return resultValue;
            if (TryLiftEnum(r, coreResult.Data.Int32,
                    out var enumResult))
                return enumResult;
            if (TryLiftFlags(r, coreResult.Data.Int32,
                    out var flagsResult))
                return flagsResult;
            if (TryLiftRecord(r, coreResult.Data.Int32,
                    out var recordResult))
                return recordResult;
            if (TryLiftVariant(r, coreResult.Data.Int32,
                    out var variantResult))
                return variantResult;
            if (TryLiftOwn(r, coreResult.Data.Int32,
                    out var ownResult))
                return ownResult;

            throw new NotSupportedException(
                "Lift of this aggregate return shape via the "
                + "interpreter is a follow-up.");
        }

        /// <summary>Attempt <c>list&lt;primitive&gt;</c> lift —
        /// returns false for any other type-ref shape so the
        /// caller can fall through to the next candidate.</summary>
        private bool TryLiftListOfPrim(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentListType list))
                return false;
            if (!list.Element.IsPrimitive) return false;
            if (list.Element.Prim == ComponentPrim.String) return false;
            if (_memory == null) return false;

            var header = ReadMemoryBytes(retAreaPtr, 8);
            var dataPtr = BitConverter.ToInt32(header, 0);
            var count = BitConverter.ToInt32(header, 4);

            // Closed-generic dispatch: the transpiler's IL does
            // the same MakeGenericMethod step, so this is pure
            // runtime reflection — the transpiler's AOT path is
            // the fast lane; the interpreter pays the reflection
            // cost per call.
            var elemCs = PrimToCs(list.Element.Prim);
            var liftOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                1,
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;
            var liftClosed = liftOpen.MakeGenericMethod(elemCs);
            // Pass the full memory snapshot so the helper's
            // bounds checks run against the real buffer length.
            result = liftClosed.Invoke(null,
                new object[] { _memory.Data, dataPtr, count });
            return true;
        }

        /// <summary>Attempt <c>option&lt;primitive&gt;</c> lift.</summary>
        private bool TryLiftOptionOfPrim(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentOptionType opt))
                return false;
            if (!opt.Inner.IsPrimitive) return false;
            if (opt.Inner.Prim == ComponentPrim.String) return false;
            if (_memory == null) return false;

            var disc = _memory.Data[retAreaPtr];
            var innerCs = PrimToCs(opt.Inner.Prim);
            var nullableT = typeof(Nullable<>).MakeGenericType(innerCs);
            if (disc == 0)
            {
                // Default Nullable<T> (HasValue = false).
                result = Activator.CreateInstance(nullableT);
                return true;
            }
            if (disc != 1)
                throw new FormatException(
                    $"Invalid option discriminant 0x{disc:X2}; expected 0 or 1.");

            // Some: payload at offset 4 for small prims (≤4 bytes).
            var payloadBytes = ReadMemoryBytes(retAreaPtr + 4, 4);
            object payload = opt.Inner.Prim switch
            {
                ComponentPrim.Bool => (object)(payloadBytes[0] != 0),
                ComponentPrim.S8   => (object)(sbyte)payloadBytes[0],
                ComponentPrim.U8   => (object)payloadBytes[0],
                ComponentPrim.S16  => (object)BitConverter.ToInt16(payloadBytes, 0),
                ComponentPrim.U16  => (object)BitConverter.ToUInt16(payloadBytes, 0),
                ComponentPrim.S32  => (object)BitConverter.ToInt32(payloadBytes, 0),
                ComponentPrim.U32  => (object)BitConverter.ToUInt32(payloadBytes, 0),
                ComponentPrim.F32  => (object)BitConverter.ToSingle(payloadBytes, 0),
                ComponentPrim.Char => (object)BitConverter.ToUInt32(payloadBytes, 0),
                _ => throw new NotSupportedException(
                    "option<" + opt.Inner.Prim + "> lift is a follow-up "
                    + "(wide primitives + strings need extra offset math)."),
            };
            result = Activator.CreateInstance(nullableT, payload);
            return true;
        }

        /// <summary>Lift <c>list&lt;string&gt;</c> via the
        /// dedicated <see cref="ListMarshal.LiftStringList"/>
        /// helper — keeps the UTF-8 chokepoint discipline.</summary>
        private bool TryLiftListOfString(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentListType list))
                return false;
            if (!list.Element.IsPrimitive) return false;
            if (list.Element.Prim != ComponentPrim.String) return false;
            if (_memory == null) return false;

            var header = ReadMemoryBytes(retAreaPtr, 8);
            var listPtr = BitConverter.ToInt32(header, 0);
            var count = BitConverter.ToInt32(header, 4);
            result = _stringEncoding == CanonOption.Kind.StringUtf16
                ? ListMarshal.LiftStringListUtf16(_memory.Data, listPtr, count)
                : ListMarshal.LiftStringList(_memory.Data, listPtr, count);
            return true;
        }

        /// <summary>Lift <c>option&lt;string&gt;</c>: disc byte
        /// at offset 0, on Some the (strPtr, strLen) at offset
        /// 4/8 → <see cref="StringMarshal.LiftUtf8"/>; on None
        /// the C# surface is <c>null</c>.</summary>
        private bool TryLiftOptionOfString(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentOptionType opt))
                return false;
            if (!opt.Inner.IsPrimitive
                || opt.Inner.Prim != ComponentPrim.String) return false;
            if (_memory == null) return false;

            var disc = _memory.Data[retAreaPtr];
            if (disc == 0) { result = null; return true; }
            if (disc != 1)
                throw new FormatException(
                    $"Invalid option discriminant 0x{disc:X2}; expected 0 or 1.");
            var strPtr = BitConverter.ToInt32(
                ReadMemoryBytes(retAreaPtr + 4, 4), 0);
            var strLen = BitConverter.ToInt32(
                ReadMemoryBytes(retAreaPtr + 8, 4), 0);
            result = LiftStringAt(strPtr, strLen);
            return true;
        }

        /// <summary>Lift a <c>tuple&lt;prim, prim, …&gt;</c> as
        /// a <c>ValueTuple&lt;…&gt;</c>. v0 supports 1–7 element
        /// tuples; 8+ element variants need the nested TRest
        /// shape the transpiler also defers.</summary>
        private bool TryLiftTupleOfPrims(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentTupleType tup))
                return false;
            var prims = new ComponentPrim[tup.Elements.Count];
            for (int i = 0; i < prims.Length; i++)
            {
                if (!tup.Elements[i].IsPrimitive) return false;
                prims[i] = tup.Elements[i].Prim;
            }
            if (_memory == null) return false;

            // For all-32-bit tuples the stride is 4 bytes — same
            // assumption the transpiler's EmitTupleReturnBody
            // makes. Wide prims (s64/u64/f64) + mixed-width
            // alignment land as the transpiler's coverage grows.
            var values = new object[prims.Length];
            for (int i = 0; i < prims.Length; i++)
                values[i] = ReadPrimAtOffset(retAreaPtr + i * 4, prims[i]);

            var csTypes = new Type[prims.Length];
            for (int i = 0; i < prims.Length; i++)
                csTypes[i] = PrimToCs(prims[i]);
            var tupleType = MakeValueTuple(csTypes);
            result = Activator.CreateInstance(tupleType, values);
            return true;
        }

        /// <summary>Lift <c>result&lt;Ok, Err&gt;</c> as
        /// <c>ValueTuple&lt;bool, Ok, Err&gt;</c> — same shape
        /// the transpiler's `ResultSide` projection produces.
        /// Disc=0 → (true, ok, default(Err)); Disc=1 → (false,
        /// default(Ok), err). Each side may be Absent (object),
        /// Primitive, or String.</summary>
        private bool TryLiftResult(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentResultType res))
                return false;
            if (!TryClassifySide(res.Ok, out var okKind, out var okPrim))
                return false;
            if (!TryClassifySide(res.Err, out var errKind, out var errPrim))
                return false;
            if (_memory == null) return false;

            var disc = _memory.Data[retAreaPtr];
            if (disc > 1)
                throw new FormatException(
                    $"Invalid result discriminant 0x{disc:X2}.");

            var okType = SideToCsType(okKind, okPrim);
            var errType = SideToCsType(errKind, errPrim);
            var tupleType = typeof(ValueTuple<,,>)
                .MakeGenericType(typeof(bool), okType, errType);
            object okValue = SideDefault(okType);
            object errValue = SideDefault(errType);
            if (disc == 0 && okKind != SideKind.Absent)
                okValue = LiftSidePayload(okKind, okPrim, retAreaPtr + 4);
            else if (disc == 1 && errKind != SideKind.Absent)
                errValue = LiftSidePayload(errKind, errPrim, retAreaPtr + 4);
            result = Activator.CreateInstance(tupleType,
                new object[] { disc == 0, okValue, errValue });
            return true;
        }

        private enum SideKind { Absent, Primitive, String }

        private static bool TryClassifySide(
            ComponentValType? raw, out SideKind kind,
            out ComponentPrim prim)
        {
            kind = SideKind.Absent;
            prim = default;
            if (raw == null) return true;
            if (!raw.Value.IsPrimitive) return false;
            if (raw.Value.Prim == ComponentPrim.String)
            { kind = SideKind.String; return true; }
            kind = SideKind.Primitive;
            prim = raw.Value.Prim;
            return true;
        }

        private static Type SideToCsType(SideKind kind, ComponentPrim prim) =>
            kind switch
            {
                SideKind.Absent => typeof(object),
                SideKind.String => typeof(string),
                SideKind.Primitive => PrimToCs(prim),
                _ => throw new InvalidOperationException(),
            };

        private static object SideDefault(Type t)
        {
            if (t.IsValueType) return Activator.CreateInstance(t)!;
            return null!;
        }

        private object LiftSidePayload(SideKind kind,
            ComponentPrim prim, int payloadOffset)
        {
            switch (kind)
            {
                case SideKind.Primitive:
                    return ReadPrimAtOffset(payloadOffset, prim);
                case SideKind.String:
                    var strPtr = BitConverter.ToInt32(
                        ReadMemoryBytes(payloadOffset, 4), 0);
                    var strLen = BitConverter.ToInt32(
                        ReadMemoryBytes(payloadOffset + 4, 4), 0);
                    return LiftStringAt(strPtr, strLen);
                default:
                    throw new InvalidOperationException(
                        "Absent side has no payload to lift.");
            }
        }

        /// <summary>Read a primitive value at a given memory
        /// offset — handles 32-bit prims (and bool / narrow
        /// ints which fit in i32 wire form). Wide prims
        /// (s64/u64/f64) are a follow-up that mirrors the
        /// transpiler's same gap.</summary>
        private object ReadPrimAtOffset(int offset, ComponentPrim prim)
        {
            var bytes = ReadMemoryBytes(offset, 4);
            return prim switch
            {
                ComponentPrim.Bool => bytes[0] != 0,
                ComponentPrim.S8   => (object)(sbyte)bytes[0],
                ComponentPrim.U8   => (object)bytes[0],
                ComponentPrim.S16  => (object)BitConverter.ToInt16(bytes, 0),
                ComponentPrim.U16  => (object)BitConverter.ToUInt16(bytes, 0),
                ComponentPrim.S32  => (object)BitConverter.ToInt32(bytes, 0),
                ComponentPrim.U32  => (object)BitConverter.ToUInt32(bytes, 0),
                ComponentPrim.F32  => (object)BitConverter.ToSingle(bytes, 0),
                ComponentPrim.Char => (object)BitConverter.ToUInt32(bytes, 0),
                _ => throw new NotSupportedException(
                    "ReadPrimAtOffset for " + prim
                    + " (wide prim) is a follow-up."),
            };
        }

        /// <summary>Lift a structural enum. Unlike the
        /// retArea-pointer aggregates, enum results return the
        /// discriminant directly on the wasm stack — narrow the
        /// i32 to the case-count-derived width per the
        /// canonical-ABI rule.</summary>
        private bool TryLiftEnum(
            ComponentValType t, int rawValue, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentEnumType en))
                return false;

            // Without a decoded WIT name to bind to a generated
            // enum type, return the raw discriminant integer —
            // the transpiler does the same fallback in
            // `ResolveEnumReturnType` when no WIT name is bound.
            var caseCount = en.Cases.Count;
            if (caseCount <= 256) result = (byte)rawValue;
            else if (caseCount <= 65536) result = (ushort)rawValue;
            else result = (uint)rawValue;
            return true;
        }

        /// <summary>Lift structural flags — same wire shape as
        /// enum (the bitmask comes back directly on the wasm
        /// stack) with a flag-count-derived width: ≤8 → byte,
        /// ≤16 → ushort, ≤32 → uint.</summary>
        private bool TryLiftFlags(
            ComponentValType t, int rawValue, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentFlagsType fl))
                return false;

            var n = fl.Flags.Count;
            if (n <= 8) result = (byte)rawValue;
            else if (n <= 16) result = (ushort)rawValue;
            else result = (uint)rawValue;
            return true;
        }

        /// <summary>Lift a record return. Without per-instance
        /// dynamic-type emission (the transpiler's
        /// EmitRecordType path) the interpreter surfaces records
        /// as <c>IReadOnlyDictionary&lt;string, object&gt;</c>
        /// keyed by WIT field name. Callers that need a typed
        /// shape should go through the transpiler.</summary>
        private bool TryLiftRecord(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentRecordType rec))
                return false;
            // v0 supports primitive fields only — same restriction
            // the transpiler enforces. Aggregate fields each add
            // their own marshaling shape.
            foreach (var f in rec.Fields)
                if (!f.Type.IsPrimitive) return false;
            if (_memory == null) return false;

            var dict = new Dictionary<string, object>(rec.Fields.Count);
            int offset = 0;
            foreach (var f in rec.Fields)
            {
                var align = PrimByteSize(f.Type.Prim);
                offset = AlignUp(offset, align);
                dict[f.Name] = ReadPrimAtOffset(retAreaPtr + offset,
                                                 f.Type.Prim);
                offset += align;
            }
            result = dict;
            return true;
        }

        /// <summary>Lift a variant return. Surfaces as a
        /// <c>(byte Tag, object? Payload)</c> tuple — Tag is the
        /// 0-indexed case ordinal, Payload is the lifted value:
        /// primitive, string, T[] for list-of-prim, or
        /// <c>IReadOnlyDictionary&lt;string, object&gt;</c> for
        /// record-of-prim. <c>null</c> for cases with no payload.
        /// Without dynamic-type emission this is the closest the
        /// interpreter gets to the transpiler's generated
        /// tagged-union class.</summary>
        private bool TryLiftVariant(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentVariantType vr))
                return false;
            // Phase 2: every payload-bearing case must be
            // primitive, list<prim>, or record-of-prim. Other
            // aggregates (nested variant, list<aggregate>) bail.
            foreach (var c in vr.Cases)
            {
                if (c.Payload == null) continue;
                if (c.Payload.Value.IsPrimitive) continue;
                if (c.Payload.Value.TypeIdx >= _component.Types.Count)
                    return false;
                var inner = _component.Types[(int)c.Payload.Value.TypeIdx];
                if (inner is ComponentListType lt
                    && lt.Element.IsPrimitive
                    && lt.Element.Prim != ComponentPrim.String) continue;
                if (inner is ComponentRecordType rt
                    && AllPrimRecord(rt)) continue;
                return false;
            }
            if (_memory == null) return false;

            // Discriminant width tracks case count.
            var caseCount = vr.Cases.Count;
            var discWidth = caseCount <= 256 ? 1
                : caseCount <= 65536 ? 2 : 4;
            // Payload alignment: max alignment over all payload-
            // bearing cases. Mirrors the transpiler's
            // EmitVariantReturnBody computation.
            int payloadAlign = 1;
            foreach (var c in vr.Cases)
            {
                if (c.Payload == null) continue;
                int a;
                if (c.Payload.Value.IsPrimitive)
                {
                    a = c.Payload.Value.Prim == ComponentPrim.String
                        ? 4 : PrimByteSize(c.Payload.Value.Prim);
                }
                else
                {
                    var inner = _component.Types[(int)c.Payload.Value.TypeIdx];
                    a = inner is ComponentRecordType rec
                        ? RecordMaxFieldAlign(rec)
                        : 4;   // list<prim>: pointer alignment
                }
                if (a > payloadAlign) payloadAlign = a;
            }
            var payloadOffset = AlignUp(discWidth, payloadAlign);

            uint disc;
            if (discWidth == 1) disc = _memory.Data[retAreaPtr];
            else if (discWidth == 2) disc = BitConverter.ToUInt16(
                _memory.Data, retAreaPtr);
            else disc = BitConverter.ToUInt32(_memory.Data, retAreaPtr);
            if (disc >= caseCount)
                throw new FormatException(
                    $"Variant discriminant {disc} out of range "
                    + $"(case count = {caseCount}).");

            var c0 = vr.Cases[(int)disc];
            object? payload = null;
            if (c0.Payload.HasValue)
            {
                var p = c0.Payload.Value;
                if (p.IsPrimitive)
                {
                    if (p.Prim == ComponentPrim.String)
                        payload = LiftStringRetArea(retAreaPtr + payloadOffset);
                    else
                        payload = ReadPrimAtOffset(
                            retAreaPtr + payloadOffset, p.Prim);
                }
                else
                {
                    var inner = _component.Types[(int)p.TypeIdx];
                    if (inner is ComponentListType lt)
                        payload = LiftListPayload(
                            retAreaPtr + payloadOffset, lt.Element.Prim);
                    else if (inner is ComponentRecordType rec)
                        payload = LiftRecordPayload(
                            retAreaPtr + payloadOffset, rec);
                }
            }
            result = ((byte)disc, payload);
            return true;
        }

        /// <summary>True iff every record field is primitive.
        /// Companion to <see cref="TryLiftVariant"/>'s record-
        /// payload classifier.</summary>
        private static bool AllPrimRecord(ComponentRecordType rec)
        {
            foreach (var f in rec.Fields)
                if (!f.Type.IsPrimitive) return false;
            return true;
        }

        /// <summary>Max field alignment in a record — drives the
        /// canonical-ABI record alignment. Mirrors the
        /// transpiler's <c>RecordAlign</c>.</summary>
        private static int RecordMaxFieldAlign(ComponentRecordType rec)
        {
            int a = 1;
            foreach (var f in rec.Fields)
            {
                if (!f.Type.IsPrimitive) continue;
                var fa = PrimByteSize(f.Type.Prim);
                if (fa > a) a = fa;
            }
            return a;
        }

        /// <summary>Lift a list-of-primitive payload at a given
        /// offset. Reads (dataPtr, count) and dispatches
        /// <see cref="ListMarshal.LiftPrim{T}"/> via reflection
        /// over the element CLR type.</summary>
        private object LiftListPayload(int offset, ComponentPrim elemPrim)
        {
            var header = ReadMemoryBytes(offset, 8);
            var dataPtr = BitConverter.ToInt32(header, 0);
            var count = BitConverter.ToInt32(header, 4);
            var elemCs = PrimToCs(elemPrim);
            var liftOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                1,
                new[] { typeof(byte[]), typeof(int), typeof(int) })!;
            var liftClosed = liftOpen.MakeGenericMethod(elemCs);
            return liftClosed.Invoke(null,
                new object[] { _memory!.Data, dataPtr, count })!;
        }

        /// <summary>Lift a record-of-primitive payload at a given
        /// offset. Mirrors <see cref="TryLiftRecord"/>'s field-by-
        /// field reader; surfaces as
        /// <c>IReadOnlyDictionary&lt;string, object&gt;</c>.</summary>
        private object LiftRecordPayload(int baseOffset, ComponentRecordType rec)
        {
            var dict = new Dictionary<string, object>(rec.Fields.Count);
            int rel = 0;
            foreach (var f in rec.Fields)
            {
                var align = PrimByteSize(f.Type.Prim);
                rel = AlignUp(rel, align);
                dict[f.Name] = ReadPrimAtOffset(
                    baseOffset + rel, f.Type.Prim);
                rel += align;
            }
            return dict;
        }

        /// <summary>Lift an <c>own&lt;R&gt;</c> return as the raw
        /// i32 handle. Users wrap it in their own resource type
        /// — the interpreter doesn't dynamically emit a sealed
        /// class the way the transpiler's EmitResourceType
        /// does. Callers that need the typed wrapper should
        /// transpile the component instead.</summary>
        private bool TryLiftOwn(
            ComponentValType t, int rawValue, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentOwnType _))
                return false;
            // own<R> returns the i32 handle directly on the wasm
            // stack — no retArea indirection.
            result = rawValue;
            return true;
        }

        private static int AlignUp(int offset, int alignment)
        {
            var rem = offset % alignment;
            return rem == 0 ? offset : offset + (alignment - rem);
        }

        /// <summary>Build a closed <c>ValueTuple&lt;…&gt;</c>
        /// type — copy of the transpiler's <c>MakeValueTuple</c>.</summary>
        private static Type MakeValueTuple(Type[] elements)
        {
            if (elements.Length == 0)
                throw new ArgumentException("Empty tuple not supported.");
            var openGeneric = elements.Length switch
            {
                1 => typeof(ValueTuple<>),
                2 => typeof(ValueTuple<,>),
                3 => typeof(ValueTuple<,,>),
                4 => typeof(ValueTuple<,,,>),
                5 => typeof(ValueTuple<,,,,>),
                6 => typeof(ValueTuple<,,,,,>),
                7 => typeof(ValueTuple<,,,,,,>),
                _ => throw new NotImplementedException(
                    "8+-element tuples (nested TRest) are a follow-up."),
            };
            return openGeneric.MakeGenericType(elements);
        }

        /// <summary>C# type for a component primitive — parallel
        /// to the transpiler's <c>PrimToCs</c>. Duplicated here
        /// rather than shared to keep the interpreter path from
        /// reaching into transpiler-only code.</summary>
        private static Type PrimToCs(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool   => typeof(bool),
            ComponentPrim.S8     => typeof(sbyte),
            ComponentPrim.U8     => typeof(byte),
            ComponentPrim.S16    => typeof(short),
            ComponentPrim.U16    => typeof(ushort),
            ComponentPrim.S32    => typeof(int),
            ComponentPrim.U32    => typeof(uint),
            ComponentPrim.S64    => typeof(long),
            ComponentPrim.U64    => typeof(ulong),
            ComponentPrim.F32    => typeof(float),
            ComponentPrim.F64    => typeof(double),
            ComponentPrim.Char   => typeof(uint),
            ComponentPrim.String => typeof(string),
            _ => throw new NotSupportedException(
                "PrimToCs for " + p + " is a follow-up."),
        };

        private static object? LiftPrimitiveFromValue(
            ComponentPrim prim, Wacs.Core.Runtime.Value v) =>
            prim switch
            {
                ComponentPrim.Bool => (object)(v.Data.Int32 != 0),
                ComponentPrim.S8   => (object)(sbyte)v.Data.Int32,
                ComponentPrim.U8   => (object)(byte)v.Data.Int32,
                ComponentPrim.S16  => (object)(short)v.Data.Int32,
                ComponentPrim.U16  => (object)(ushort)v.Data.Int32,
                ComponentPrim.S32  => (object)v.Data.Int32,
                ComponentPrim.U32  => (object)v.Data.UInt32,
                ComponentPrim.S64  => (object)v.Data.Int64,
                ComponentPrim.U64  => (object)v.Data.UInt64,
                ComponentPrim.F32  => (object)v.Data.Float32,
                ComponentPrim.F64  => (object)v.Data.Float64,
                ComponentPrim.Char => (object)v.Data.UInt32,
                _ => throw new NotSupportedException(
                    "Primitive lift for " + prim + " is a follow-up."),
            };

        private static bool IsStringRef(ComponentValType t) =>
            t.IsPrimitive && t.Prim == ComponentPrim.String;

        private string LiftStringRetArea(int retAreaPtr)
        {
            if (_memory == null)
                throw new InvalidOperationException(
                    "String-returning component requires the core "
                    + "module to export a memory.");
            var memBytes = ReadMemoryBytes(retAreaPtr, 8);
            var strPtr = BitConverter.ToInt32(memBytes, 0);
            var strLen = BitConverter.ToInt32(memBytes, 4);
            return LiftStringAt(strPtr, strLen);
        }

        /// <summary>Decode a string at <paramref name="strPtr"/>
        /// per the per-invoke <see cref="_stringEncoding"/>.
        /// <paramref name="strLen"/> is bytes for UTF-8 and u16
        /// code units for UTF-16 (canonical-ABI rule).</summary>
        private string LiftStringAt(int strPtr, int strLen)
        {
            if (_stringEncoding == CanonOption.Kind.StringUtf16)
                return StringMarshal.LiftUtf16(
                    _memory!.Data, strPtr, strLen);
            return StringMarshal.LiftUtf8(
                _memory!.Data, strPtr, strLen);
        }

        /// <summary>Snapshot a slice of the core module's
        /// linear memory into a fresh byte array. Component-side
        /// callers shouldn't peek at the underlying memory
        /// representation directly — this routes the lookup
        /// through the standard MemoryInstance accessors so
        /// growth / shared-memory invariants stay
        /// MemoryInstance's concern.</summary>
        private byte[] ReadMemoryBytes(int offset, int length)
        {
            var snapshot = new byte[length];
            for (int i = 0; i < length; i++)
                snapshot[i] = _memory!.Data[offset + i];
            return snapshot;
        }

    }
}
