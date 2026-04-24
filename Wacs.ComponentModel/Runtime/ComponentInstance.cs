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
        private readonly ModuleInstance _coreInstance;
        private MemoryInstance? _memory;

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

            return LiftResult(fn, coreFuncType, coreResults);
        }

        // ---- Lower (managed → core wire) -----------------------------

        private object[] LowerArgs(ComponentFuncType fn, object?[] userArgs)
        {
            // Primitive params pass through unchanged. Aggregate
            // params (string, list<prim>) need cabi_realloc on the
            // guest side — implementing that via the interpreter
            // requires calling the guest's realloc through the
            // same WasmRuntime, then writing bytes into the
            // exported memory. Deferred to a follow-up; v0 errors
            // on aggregate params instead of half-implementing.
            if (fn.Params.Count != userArgs.Length)
                throw new ArgumentException(
                    $"Expected {fn.Params.Count} args, got "
                    + $"{userArgs.Length}.");

            var lowered = new object[fn.Params.Count];
            for (int i = 0; i < fn.Params.Count; i++)
            {
                var pt = fn.Params[i].Type;
                if (!pt.IsPrimitive)
                    throw new NotSupportedException(
                        "ComponentInstance.Invoke v0 only handles "
                        + "primitive params. Lowering aggregate args "
                        + "via cabi_realloc is a follow-up.");
                lowered[i] = LowerPrimitive(pt.Prim, userArgs[i])!;
            }
            return lowered;
        }

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

            // Other aggregates (list, option, result, tuple, …)
            // route through the same StringMarshal / ListMarshal
            // helpers the transpiler emits IL against — not yet
            // wired through the interpreter path.
            throw new NotSupportedException(
                "Lift of non-string aggregate return via the "
                + "interpreter is a follow-up. The transpiler "
                + "path covers these end-to-end.");
        }

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
            return StringMarshal.LiftUtf8(
                ReadMemoryBytes(strPtr, strLen), 0, strLen);
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
