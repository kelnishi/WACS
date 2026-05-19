// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core.Runtime;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Binds canon-async builtins (parsed Phase 2 <see cref="CanonEntry"/>
    /// subclasses) to <see cref="AsyncDispatcher"/> methods on a
    /// <see cref="WasmRuntime"/>. Mirrors the <c>CanonResourceBinder</c>
    /// pattern: walk the parsed canon entries, register a host
    /// function delegate per entry under the agreed (module, name)
    /// pair, and let core-module instantiation resolve them as
    /// imports.
    ///
    /// <para><b>Naming convention:</b> the wasm Component Model
    /// 0.3.0-rc spec doesn't fix the import-name convention
    /// wit-component emits for canon-async builtins — that's a
    /// tooling decision that's still settling. This binder uses a
    /// documented placeholder convention by default and accepts a
    /// <see cref="NameResolver"/> override so embedders can adopt
    /// whatever convention their toolchain produces:</para>
    /// <code>
    /// module = "[canon]"
    /// name   = "[&lt;op-kebab&gt;]" or "[&lt;op-kebab&gt;]#&lt;typeidx&gt;"
    ///          for ops carrying a typeidx (stream.*, future.*).
    /// </code>
    /// <para>Examples: <c>[task-cancel]</c>, <c>[stream-new]#5</c>,
    /// <c>[error-context-debug-message]</c>.</para>
    ///
    /// <para><b>What's bound in Slice E:</b> the canon-async ops
    /// with trivial core-func signatures — handle allocation,
    /// handle drop, marker ops (task.cancel, subtask.drop),
    /// backpressure, error-context, waitable-set membership.
    /// Skipped: ops requiring memory access (stream.read/.write,
    /// future.read/.write, error-context.new — the message read
    /// goes through memory) or result-list marshaling (task.return,
    /// context.get/.set). Those land with the lift adapter in
    /// Slice F.</para>
    /// </summary>
    public static class CanonAsyncBinder
    {
        /// <summary>(module, name) pair an embedder picks per canon
        /// entry. Return <c>(null, null)</c> to skip an entry.</summary>
        public delegate (string? Module, string? Name) NameResolver(CanonEntry entry);

        /// <summary>Default placeholder convention. Subject to change
        /// when wit-component's actual emit convention is settled.</summary>
        public static (string?, string?) DefaultNameResolver(CanonEntry e)
        {
            switch (e)
            {
                case CanonTaskCancel _:           return ("[canon]", "[task-cancel]");
                case CanonSubtaskDrop _:          return ("[canon]", "[subtask-drop]");
                case CanonSubtaskCancel _:        return ("[canon]", "[subtask-cancel]");
                case CanonBackpressureOp bp:      return ("[canon]", $"[backpressure-{BackpressureSuffix(bp.Op)}]");
                case CanonStreamOp s:             return ("[canon]", $"[stream-{StreamSuffix(s.Op)}]#{s.StreamTypeIdx}");
                case CanonFutureOp f:             return ("[canon]", $"[future-{FutureSuffix(f.Op)}]#{f.FutureTypeIdx}");
                case CanonErrorContextOp ec:      return ("[canon]", $"[error-context-{ErrorContextSuffix(ec.Op)}]");
                case CanonWaitableSetOp ws:       return ("[canon]", $"[waitable-set-{WaitableSetSuffix(ws.Op)}]");
                case CanonWaitableJoin _:         return ("[canon]", "[waitable-join]");
                case CanonThreadYield _:          return ("[canon]", "[thread-yield]");
                // task.return: bind iff the resultlist is empty,
                // a primitive valtype, a string (Slice I.1), a
                // list-of-primitive (Slice I.2), or an
                // option/result-of-primitive (Slice I.3). For
                // typeidx-referenced aggregates the name resolver
                // alone can't validate — TryBuildDelegate makes
                // the final call via `Types` lookup.
                case CanonTaskReturn tr:
                    if (tr.Result == null
                        || IsBindablePrimitive(tr.Result.Value)
                        || IsBindableString(tr.Result.Value)
                        || tr.Result.Value.IsPrimitive == false)
                        return ("[canon]", "[task-return]");
                    return (null, null);
                // context.{get,set}: bind iff valtype is a
                // primitive. Slot index is part of the name so
                // each (slot, valtype) gets its own binding.
                case CanonContextOp cx:
                    if (IsBindablePrimitive(cx.ValType))
                        return ("[canon]",
                            $"[context-{(cx.Op == CanonContextOp.Kind.Get ? "get" : "set")}]#{cx.Index}");
                    return (null, null);
                default:                          return (null, null);
            }
        }

        private static bool IsBindablePrimitive(ComponentValType v)
        {
            if (!v.IsPrimitive) return false;
            switch (v.Prim)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                case ComponentPrim.F32:
                case ComponentPrim.F64:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True iff <paramref name="v"/> is the
        /// component-model <c>string</c> primitive — handled
        /// separately from <see cref="IsBindablePrimitive"/>
        /// because the lift path needs memory access (the
        /// dispatcher's <c>Memory</c> + <c>StringEncoding</c>
        /// properties) and a different delegate signature
        /// (<c>(i32 ptr, i32 len)</c> instead of the primitive's
        /// single core value).</summary>
        private static bool IsBindableString(ComponentValType v) =>
            v.IsPrimitive && v.Prim == ComponentPrim.String;

        private static string BackpressureSuffix(CanonBackpressureOp.Kind k) => k switch
        {
            CanonBackpressureOp.Kind.Set => "set",
            CanonBackpressureOp.Kind.Inc => "inc",
            CanonBackpressureOp.Kind.Dec => "dec",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        private static string StreamSuffix(CanonStreamOp.Kind k) => k switch
        {
            CanonStreamOp.Kind.New           => "new",
            CanonStreamOp.Kind.Read          => "read",
            CanonStreamOp.Kind.Write         => "write",
            CanonStreamOp.Kind.CancelRead    => "cancel-read",
            CanonStreamOp.Kind.CancelWrite   => "cancel-write",
            CanonStreamOp.Kind.DropReadable  => "drop-readable",
            CanonStreamOp.Kind.DropWritable  => "drop-writable",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        private static string FutureSuffix(CanonFutureOp.Kind k) => k switch
        {
            CanonFutureOp.Kind.New           => "new",
            CanonFutureOp.Kind.Read          => "read",
            CanonFutureOp.Kind.Write         => "write",
            CanonFutureOp.Kind.CancelRead    => "cancel-read",
            CanonFutureOp.Kind.CancelWrite   => "cancel-write",
            CanonFutureOp.Kind.DropReadable  => "drop-readable",
            CanonFutureOp.Kind.DropWritable  => "drop-writable",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        private static string ErrorContextSuffix(CanonErrorContextOp.Kind k) => k switch
        {
            CanonErrorContextOp.Kind.New          => "new",
            CanonErrorContextOp.Kind.DebugMessage => "debug-message",
            CanonErrorContextOp.Kind.Drop         => "drop",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        private static string WaitableSetSuffix(CanonWaitableSetOp.Kind k) => k switch
        {
            CanonWaitableSetOp.Kind.New  => "new",
            CanonWaitableSetOp.Kind.Wait => "wait",
            CanonWaitableSetOp.Kind.Poll => "poll",
            CanonWaitableSetOp.Kind.Drop => "drop",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        /// <summary>
        /// Walk the component's canon-async entries and bind a host
        /// function for each to the supplied <paramref name="dispatcher"/>.
        /// Call BEFORE <see cref="WasmRuntime.InstantiateModule"/>
        /// so the inner core module's import resolution finds them.
        /// Returns the list of (module, name) pairs that were bound,
        /// so callers can surface what was wired for diagnostics.
        /// </summary>
        public static List<(string Module, string Name, CanonEntry Entry)> BindImports(
            WasmRuntime runtime,
            IReadOnlyList<CanonEntry> canonEntries,
            AsyncDispatcher dispatcher,
            NameResolver? nameResolver = null)
        {
            nameResolver ??= DefaultNameResolver;
            var bound = new List<(string, string, CanonEntry)>();

            foreach (var entry in canonEntries)
            {
                var (mod, name) = nameResolver(entry);
                if (mod == null || name == null) continue;

                if (TryBuildDelegate(entry, dispatcher, out var del))
                {
                    runtime.BindHostFunction((mod, name), del!);
                    bound.Add((mod, name, entry));
                }
            }

            return bound;
        }

        /// <summary>
        /// Build the typed host-function delegate for a single
        /// canon-async <paramref name="entry"/> over the supplied
        /// <paramref name="dispatcher"/>. Returns null when the
        /// entry kind isn't currently buildable (e.g. aggregate-
        /// typed task.return / context with a non-primitive
        /// valtype — those need the canon-ABI lift adapter).
        ///
        /// <para>Public so the shim-module recognizer can consume
        /// the same delegate-construction logic without
        /// duplicating the per-shape switch.</para>
        /// </summary>
        public static Delegate? TryBuildDelegateForEntry(
            CanonEntry entry, AsyncDispatcher dispatcher)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            return TryBuildDelegate(entry, dispatcher, out var del) ? del : null;
        }

        // Build the typed delegate for a canon-async entry. The
        // signatures match the spec's "produces a (core func)"
        // shape for each canon form.
        private static bool TryBuildDelegate(
            CanonEntry entry, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            switch (entry)
            {
                // () -> ()
                case CanonTaskCancel _:
                    del = (Action<ExecContext>)((_) => d.TaskCancel(null!));
                    return true;

                // (i32) -> ()
                case CanonSubtaskDrop _:
                    del = (Action<ExecContext, int>)((_, h) => d.SubtaskDrop(h));
                    return true;

                // (i32) -> () with async? captured at bind time
                case CanonSubtaskCancel sc:
                    del = (Action<ExecContext, int>)((_, h) =>
                        d.SubtaskCancel(h, sc.Async));
                    return true;

                // () -> ()
                case CanonBackpressureOp bp:
                    switch (bp.Op)
                    {
                        case CanonBackpressureOp.Kind.Set:
                            del = (Action<ExecContext>)((_) => d.BackpressureSet());
                            return true;
                        case CanonBackpressureOp.Kind.Inc:
                            del = (Action<ExecContext>)((_) => d.BackpressureInc());
                            return true;
                        case CanonBackpressureOp.Kind.Dec:
                            del = (Action<ExecContext>)((_) => d.BackpressureDec());
                            return true;
                    }
                    return false;

                // () -> i32   (stream.new t — typeidx captured)
                case CanonStreamOp { Op: CanonStreamOp.Kind.New } sn:
                    {
                        int t = (int)sn.StreamTypeIdx;
                        del = (Func<ExecContext, int>)((_) => d.StreamNew(t));
                        return true;
                    }

                // (i32) -> i32   (drop-readable/drop-writable — returns 0/1 success flag as i32)
                case CanonStreamOp { Op: CanonStreamOp.Kind.DropReadable }:
                    del = (Func<ExecContext, int, int>)((_, h) =>
                        d.StreamDropReadable(h) ? 1 : 0);
                    return true;
                case CanonStreamOp { Op: CanonStreamOp.Kind.DropWritable }:
                    del = (Func<ExecContext, int, int>)((_, h) =>
                        d.StreamDropWritable(h) ? 1 : 0);
                    return true;
                case CanonStreamOp { Op: CanonStreamOp.Kind.CancelRead } scr:
                    {
                        bool asyncFlag = scr.Async ?? false;
                        del = (Func<ExecContext, int, int>)((_, h) =>
                            d.StreamCancelRead(h, asyncFlag) ? 1 : 0);
                        return true;
                    }
                case CanonStreamOp { Op: CanonStreamOp.Kind.CancelWrite } scw:
                    {
                        bool asyncFlag = scw.Async ?? false;
                        del = (Func<ExecContext, int, int>)((_, h) =>
                            d.StreamCancelWrite(h, asyncFlag) ? 1 : 0);
                        return true;
                    }

                // Future: same shape as Stream
                case CanonFutureOp { Op: CanonFutureOp.Kind.New } fn:
                    {
                        int t = (int)fn.FutureTypeIdx;
                        del = (Func<ExecContext, int>)((_) => d.FutureNew(t));
                        return true;
                    }
                case CanonFutureOp { Op: CanonFutureOp.Kind.DropReadable }:
                    del = (Func<ExecContext, int, int>)((_, h) =>
                        d.FutureDropReadable(h) ? 1 : 0);
                    return true;
                case CanonFutureOp { Op: CanonFutureOp.Kind.DropWritable }:
                    del = (Func<ExecContext, int, int>)((_, h) =>
                        d.FutureDropWritable(h) ? 1 : 0);
                    return true;
                case CanonFutureOp { Op: CanonFutureOp.Kind.CancelRead } fcr:
                    {
                        bool asyncFlag = fcr.Async ?? false;
                        del = (Func<ExecContext, int, int>)((_, h) =>
                            d.FutureCancelRead(h, asyncFlag) ? 1 : 0);
                        return true;
                    }
                case CanonFutureOp { Op: CanonFutureOp.Kind.CancelWrite } fcw:
                    {
                        bool asyncFlag = fcw.Async ?? false;
                        del = (Func<ExecContext, int, int>)((_, h) =>
                            d.FutureCancelWrite(h, asyncFlag) ? 1 : 0);
                        return true;
                    }

                // (i32) -> i32  (error-context.drop — handle in, 0/1 out)
                case CanonErrorContextOp { Op: CanonErrorContextOp.Kind.Drop }:
                    del = (Func<ExecContext, int, int>)((_, h) =>
                        d.ErrorContextDrop(h) ? 1 : 0);
                    return true;
                // error-context.new / debug-message read/write strings
                // through component memory — Slice F (lift adapter).
                case CanonErrorContextOp _:
                    return false;

                // () -> i32   (waitable-set.new)
                case CanonWaitableSetOp { Op: CanonWaitableSetOp.Kind.New }:
                    del = (Func<ExecContext, int>)((_) => d.WaitableSetNew());
                    return true;
                // (i32) -> i32   (waitable-set.drop, returns success)
                case CanonWaitableSetOp { Op: CanonWaitableSetOp.Kind.Drop }:
                    del = (Func<ExecContext, int, int>)((_, h) =>
                        d.WaitableSetDrop(h) ? 1 : 0);
                    return true;
                // (i32, i32) -> i32   (waitable-set.wait set memidx)
                //   blocks until a member becomes deliverable; returns
                //   the deliverable handle (0 for empty set).
                case CanonWaitableSetOp { Op: CanonWaitableSetOp.Kind.Wait } wsw:
                    {
                        bool cancellable = wsw.Cancellable ?? false;
                        del = (Func<ExecContext, int, int, int>)((_, set, mem) =>
                            d.WaitableSetWait(null!, set, mem, cancellable));
                        return true;
                    }
                // (i32, i32) -> i32   (waitable-set.poll set memidx)
                //   non-blocking; returns 0 (canon null) when no
                //   member is deliverable.
                case CanonWaitableSetOp { Op: CanonWaitableSetOp.Kind.Poll } wsp:
                    {
                        bool cancellable = wsp.Cancellable ?? false;
                        del = (Func<ExecContext, int, int, int>)((_, set, mem) =>
                            d.WaitableSetPoll(null!, set, mem, cancellable));
                        return true;
                    }

                // (i32, i32) -> ()    (waitable.join: set, member)
                case CanonWaitableJoin _:
                    del = (Action<ExecContext, int, int>)((_, set, member) =>
                        d.WaitableJoin(set, member));
                    return true;

                // () -> ()  (thread.yield — synchronous no-op today)
                case CanonThreadYield ty:
                    {
                        bool cancellable = ty.Cancellable;
                        del = (Action<ExecContext>)((_) =>
                            d.ThreadYield(null!, cancellable));
                        return true;
                    }

                // task.return rs opts — bind for primitive
                // resultlists. Aggregate / string results need
                // the canon-ABI lift adapter and stay deferred.
                case CanonTaskReturn tr:
                    return TryBuildTaskReturn(tr, d, out del);

                // context.get v i / context.set v i — bind for
                // primitive valtypes.
                case CanonContextOp cx:
                    return TryBuildContextOp(cx, d, out del);

                default:
                    return false;
            }
        }

        // task.return shape: () for empty resultlist; (T) for a
        // single-primitive result. T is unwrapped and forwarded
        // to dispatcher.TaskReturn(object?). For string, the
        // delegate takes (i32 ptr, i32 len) per the canon-ABI
        // flat-lowering rules; the lift adapter reads the bytes
        // from the dispatcher's Memory using the resolved
        // StringEncoding.
        private static bool TryBuildTaskReturn(
            CanonTaskReturn tr, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            if (tr.Result == null)
            {
                del = (Action<ExecContext>)((_) => d.TaskReturn(null!, null));
                return true;
            }
            // Typeidx-ref aggregate: resolve via dispatcher.Types
            // and dispatch to the per-shape sub-builder. Slice I.2/I.3
            // cover list/option/result with primitive payloads.
            if (!tr.Result.Value.IsPrimitive)
            {
                // Slice K3: if the bindgen registered this typeidx
                // against a WIT ident with a matching
                // [ComponentLifter]-decorated method, use the
                // typed lifter directly — bypasses the per-arity
                // fallback and supports arbitrary heterogeneous
                // record/variant shapes the per-arity helpers
                // decline.
                if (d.TryGetTypedLifterForTypeIdx(
                        tr.Result.Value.TypeIdx, out var typed)
                    && typed != null)
                {
                    del = typed;
                    return true;
                }
                var defType = ResolveType(d, tr.Result.Value.TypeIdx);
                return defType switch
                {
                    ComponentListType list =>
                        TryBuildTaskReturnList(list, d, out del),
                    ComponentOptionType opt =>
                        TryBuildTaskReturnOption(opt, d, out del),
                    ComponentResultType res =>
                        TryBuildTaskReturnResult(res, d, out del),
                    ComponentTupleType tup =>
                        TryBuildTaskReturnTuple(tup, d, out del),
                    ComponentEnumType en =>
                        TryBuildTaskReturnEnum(en, d, out del),
                    ComponentFlagsType fl =>
                        TryBuildTaskReturnFlags(fl, d, out del),
                    ComponentRecordType rec =>
                        TryBuildTaskReturnRecord(rec, d, out del),
                    ComponentVariantType variant =>
                        TryBuildTaskReturnVariant(variant, d, out del),
                    _ => false,
                };
            }
            switch (tr.Result.Value.Prim)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                    del = (Action<ExecContext, int>)((_, x) =>
                        d.TaskReturn(null!, x));
                    return true;
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                    del = (Action<ExecContext, long>)((_, x) =>
                        d.TaskReturn(null!, x));
                    return true;
                case ComponentPrim.F32:
                    del = (Action<ExecContext, float>)((_, x) =>
                        d.TaskReturn(null!, x));
                    return true;
                case ComponentPrim.F64:
                    del = (Action<ExecContext, double>)((_, x) =>
                        d.TaskReturn(null!, x));
                    return true;
                case ComponentPrim.String:
                    del = (Action<ExecContext, int, int>)((_, ptr, len) =>
                    {
                        var memory = d.Memory
                            ?? throw new InvalidOperationException(
                                "task.return string: dispatcher.Memory not set. " +
                                "ComponentInstance must assign Memory after " +
                                "core-module instantiation before this call site is reachable.");
                        var lifted = d.StringEncoding switch
                        {
                            CanonOption.Kind.StringUtf16 =>
                                StringMarshal.LiftUtf16(memory, ptr, len),
                            CanonOption.Kind.StringLatin1OrUtf16 =>
                                StringMarshal.LiftLatin1OrUtf16(memory, ptr, len),
                            _ => StringMarshal.LiftUtf8(memory, ptr, len),
                        };
                        d.TaskReturn(null!, lifted);
                    });
                    return true;
                default:
                    return false;
            }
        }

        // Look up a typeidx in the dispatcher's type table.
        // Returns null when types aren't wired or the index is
        // out of range — caller bails out of the delegate build.
        private static DefTypeEntry? ResolveType(
            AsyncDispatcher d, uint typeIdx)
        {
            var types = d.Types;
            if (types == null || typeIdx >= types.Count) return null;
            return types[(int)typeIdx];
        }

        // Memory accessor with the same "must be set" diagnostic
        // as the string lift — reused by list/option/result paths.
        private static Wacs.Core.Runtime.Types.MemoryInstance RequireMemory(
            AsyncDispatcher d, string canonOpName)
        {
            return d.Memory
                ?? throw new InvalidOperationException(
                    $"{canonOpName}: dispatcher.Memory not set. " +
                    "ComponentInstance must assign Memory after " +
                    "core-module instantiation.");
        }

        // task.return list<T> for primitive T: delegate takes
        // (i32 ptr, i32 count) per canon-ABI flat-lowering rules.
        // The lift adapter reads count elements of T from memory
        // at ptr via ListMarshal.LiftPrim<T>, then forwards the
        // resulting CLR T[] to TaskReturn.
        private static bool TryBuildTaskReturnList(
            ComponentListType list, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            if (!list.Element.IsPrimitive) return false;
            switch (list.Element.Prim)
            {
                case ComponentPrim.U8:
                case ComponentPrim.S8:
                    del = (Action<ExecContext, int, int>)((_, ptr, count) =>
                        d.TaskReturn(null!,
                            ListMarshal.LiftPrim<byte>(
                                RequireMemory(d, "task.return list<u8>"), ptr, count)));
                    return true;
                case ComponentPrim.U16:
                case ComponentPrim.S16:
                    del = (Action<ExecContext, int, int>)((_, ptr, count) =>
                        d.TaskReturn(null!,
                            ListMarshal.LiftPrim<ushort>(
                                RequireMemory(d, "task.return list<u16>"), ptr, count)));
                    return true;
                case ComponentPrim.U32:
                case ComponentPrim.S32:
                    del = (Action<ExecContext, int, int>)((_, ptr, count) =>
                        d.TaskReturn(null!,
                            ListMarshal.LiftPrim<uint>(
                                RequireMemory(d, "task.return list<u32>"), ptr, count)));
                    return true;
                case ComponentPrim.U64:
                case ComponentPrim.S64:
                    del = (Action<ExecContext, int, int>)((_, ptr, count) =>
                        d.TaskReturn(null!,
                            ListMarshal.LiftPrim<ulong>(
                                RequireMemory(d, "task.return list<u64>"), ptr, count)));
                    return true;
                case ComponentPrim.F32:
                    del = (Action<ExecContext, int, int>)((_, ptr, count) =>
                        d.TaskReturn(null!,
                            ListMarshal.LiftPrim<float>(
                                RequireMemory(d, "task.return list<f32>"), ptr, count)));
                    return true;
                case ComponentPrim.F64:
                    del = (Action<ExecContext, int, int>)((_, ptr, count) =>
                        d.TaskReturn(null!,
                            ListMarshal.LiftPrim<double>(
                                RequireMemory(d, "task.return list<f64>"), ptr, count)));
                    return true;
                default:
                    return false;
            }
        }

        // task.return option<T> for primitive T: flat-lowered to
        // (i32 disc, T payload). Discriminant 0 = none, 1 = some.
        private static bool TryBuildTaskReturnOption(
            ComponentOptionType opt, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            if (!opt.Inner.IsPrimitive) return false;
            switch (opt.Inner.Prim)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                    del = (Action<ExecContext, int, int>)((_, disc, payload) =>
                        d.TaskReturn(null!,
                            disc == 0 ? (int?)null : payload));
                    return true;
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                    del = (Action<ExecContext, int, long>)((_, disc, payload) =>
                        d.TaskReturn(null!,
                            disc == 0 ? (long?)null : payload));
                    return true;
                case ComponentPrim.F32:
                    del = (Action<ExecContext, int, float>)((_, disc, payload) =>
                        d.TaskReturn(null!,
                            disc == 0 ? (float?)null : payload));
                    return true;
                case ComponentPrim.F64:
                    del = (Action<ExecContext, int, double>)((_, disc, payload) =>
                        d.TaskReturn(null!,
                            disc == 0 ? (double?)null : payload));
                    return true;
                default:
                    return false;
            }
        }

        // task.return tuple<T, T, ...> for arity 2-4 with all
        // elements of the same primitive type T. Flat-lowered to
        // (T, T, ...) — one core value per element. Materialized
        // as a closed-generic ValueTuple<T, T, ...> — AOT-safe.
        //
        // Slice I.4 scope: same-primitive same-width tuples. Mixed-
        // width and aggregate-element tuples need either per-shape
        // enumeration or the extensibility hook deferred to a
        // later slice (alongside record/variant lift).
        private static bool TryBuildTaskReturnTuple(
            ComponentTupleType tup, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            int arity = tup.Elements.Count;
            if (arity < 2 || arity > 4) return false;
            if (!tup.Elements[0].IsPrimitive) return false;
            var prim = tup.Elements[0].Prim;
            for (int i = 1; i < arity; i++)
            {
                if (!tup.Elements[i].IsPrimitive) return false;
                if (tup.Elements[i].Prim != prim) return false;
            }
            switch (prim)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                    return BuildIntTuple(arity, d, out del);
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                    return BuildLongTuple(arity, d, out del);
                case ComponentPrim.F32:
                    return BuildFloatTuple(arity, d, out del);
                case ComponentPrim.F64:
                    return BuildDoubleTuple(arity, d, out del);
                default:
                    return false;
            }
        }

        private static bool BuildIntTuple(
            int arity, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, int, int>)((_, a, b) =>
                        d.TaskReturn(null!, (a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, int, int, int>)((_, a, b, c) =>
                        d.TaskReturn(null!, (a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, int, int, int, int>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, (a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        private static bool BuildLongTuple(
            int arity, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, long, long>)((_, a, b) =>
                        d.TaskReturn(null!, (a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, long, long, long>)((_, a, b, c) =>
                        d.TaskReturn(null!, (a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, long, long, long, long>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, (a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        private static bool BuildFloatTuple(
            int arity, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, float, float>)((_, a, b) =>
                        d.TaskReturn(null!, (a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, float, float, float>)((_, a, b, c) =>
                        d.TaskReturn(null!, (a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, float, float, float, float>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, (a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        private static bool BuildDoubleTuple(
            int arity, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, double, double>)((_, a, b) =>
                        d.TaskReturn(null!, (a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, double, double, double>)((_, a, b, c) =>
                        d.TaskReturn(null!, (a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, double, double, double, double>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, (a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        // task.return result<T,E> for primitive T/E: flat-lowered
        // to (i32 disc, T okPayload, E errPayload). Discriminant
        // 0 = ok, 1 = err. Both payload slots are passed; the
        // adapter picks the live one based on disc. For typed
        // host consumption, the CLR-side result is materialized
        // as a 3-element ValueTuple (bool isOk, T ok, E err) —
        // simple, AOT-safe, no custom Result&lt;T,E&gt; type needed.
        //
        // The spec's result has either side optional (result,
        // result<T>, result<_,E>, result<T,E>). For now we only
        // bind result<T,E> with both sides primitive; the
        // single-sided variants would have a different wire
        // shape (no slot for the missing side).
        private static bool TryBuildTaskReturnResult(
            ComponentResultType res, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            // Phase I.3 covers the both-sides-present primitive
            // case. Other shapes (one side empty, aggregate
            // payloads) need further per-shape lowering.
            if (res.Ok == null || res.Err == null) return false;
            if (!res.Ok.Value.IsPrimitive || !res.Err.Value.IsPrimitive)
                return false;
            // Specialize on (Ok, Err) prim pair. The cross-product
            // is too large to enumerate cleanly; cover the common
            // case: both Ok and Err same width (most components
            // use result<u32, u32> or similar).
            if (res.Ok.Value.Prim != res.Err.Value.Prim) return false;
            switch (res.Ok.Value.Prim)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                    del = (Action<ExecContext, int, int, int>)((_, disc, ok, err) =>
                        d.TaskReturn(null!, (disc == 0, ok, err)));
                    return true;
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                    del = (Action<ExecContext, int, long, long>)((_, disc, ok, err) =>
                        d.TaskReturn(null!, (disc == 0, ok, err)));
                    return true;
                case ComponentPrim.F32:
                    del = (Action<ExecContext, int, float, float>)((_, disc, ok, err) =>
                        d.TaskReturn(null!, (disc == 0, ok, err)));
                    return true;
                case ComponentPrim.F64:
                    del = (Action<ExecContext, int, double, double>)((_, disc, ok, err) =>
                        d.TaskReturn(null!, (disc == 0, ok, err)));
                    return true;
                default:
                    return false;
            }
        }

        // context.get v i  ->  () -> v
        // context.set v i  ->  (v) -> ()
        // The slot index is captured at bind time; only the typed
        // value crosses the runtime boundary at each call.
        private static bool TryBuildContextOp(
            CanonContextOp cx, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            if (!cx.ValType.IsPrimitive) return false;
            int slot = (int)cx.Index;
            switch (cx.Op)
            {
                case CanonContextOp.Kind.Get:
                    switch (cx.ValType.Prim)
                    {
                        case ComponentPrim.S32:
                        case ComponentPrim.U32:
                            del = (Func<ExecContext, int>)((_) =>
                                d.ContextGet(slot).Data.Int32);
                            return true;
                        case ComponentPrim.S64:
                        case ComponentPrim.U64:
                            del = (Func<ExecContext, long>)((_) =>
                                d.ContextGet(slot).Data.Int64);
                            return true;
                        case ComponentPrim.F32:
                            del = (Func<ExecContext, float>)((_) =>
                                d.ContextGet(slot).Data.Float32);
                            return true;
                        case ComponentPrim.F64:
                            del = (Func<ExecContext, double>)((_) =>
                                d.ContextGet(slot).Data.Float64);
                            return true;
                        default:
                            return false;
                    }
                case CanonContextOp.Kind.Set:
                    switch (cx.ValType.Prim)
                    {
                        case ComponentPrim.S32:
                        case ComponentPrim.U32:
                            del = (Action<ExecContext, int>)((_, x) =>
                                d.ContextSet(slot, new Wacs.Core.Runtime.Value(x)));
                            return true;
                        case ComponentPrim.S64:
                        case ComponentPrim.U64:
                            del = (Action<ExecContext, long>)((_, x) =>
                                d.ContextSet(slot, new Wacs.Core.Runtime.Value(x)));
                            return true;
                        case ComponentPrim.F32:
                            del = (Action<ExecContext, float>)((_, x) =>
                                d.ContextSet(slot, new Wacs.Core.Runtime.Value(x)));
                            return true;
                        case ComponentPrim.F64:
                            del = (Action<ExecContext, double>)((_, x) =>
                                d.ContextSet(slot, new Wacs.Core.Runtime.Value(x)));
                            return true;
                        default:
                            return false;
                    }
                default:
                    return false;
            }
        }

        // task.return enum: flat-lowered to a single i32
        // discriminant per canon-ABI (FlatLowering.FlatCountDef
        // returns 1 for enum). The lift adapter materializes the
        // discriminant as a plain int — the named-case spelling
        // lives in the WIT-side metadata. Host code that wants a
        // CLR enum value reaches for the typed lifter generated
        // by the source generator (Session 3) — here we surface
        // the raw discriminant so the binder remains usable from
        // generic untyped invocation paths.
        private static bool TryBuildTaskReturnEnum(
            ComponentEnumType en, AsyncDispatcher d, out Delegate? del)
        {
            // Validate the discriminant is in-range against the
            // case count; out-of-range guest-supplied discriminants
            // are a wire-protocol error.
            int caseCount = en.Cases.Count;
            del = (Action<ExecContext, int>)((_, disc) =>
            {
                if ((uint)disc >= (uint)caseCount)
                    throw new InvalidOperationException(
                        $"task.return enum: discriminant {disc} " +
                        $"out of range for enum with {caseCount} cases");
                d.TaskReturn(null!, disc);
            });
            return true;
        }

        // task.return record { f1: T, …, fN: T } where every
        // field is the same primitive type and arity is 2-4. The
        // canon-ABI flat-lowering places each field consecutively
        // in the core call params (FlatLowering.FlatCountDef
        // returns the field-count sum). The lift materializes a
        // CLR IReadOnlyDictionary<string, object?> keyed by field
        // name — boxes the primitive but task.return is the cold
        // path (one call per task completion). Heterogeneous-field
        // records and >4-arity records are deferred to the Session
        // 3 source-gen typed-record lifters.
        private static bool TryBuildTaskReturnRecord(
            ComponentRecordType rec, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            int arity = rec.Fields.Count;
            if (arity < 2 || arity > 4) return false;
            if (!rec.Fields[0].Type.IsPrimitive) return false;
            var prim = rec.Fields[0].Type.Prim;
            for (int i = 1; i < arity; i++)
            {
                if (!rec.Fields[i].Type.IsPrimitive) return false;
                if (rec.Fields[i].Type.Prim != prim) return false;
            }
            // Snapshot field names so the closure doesn't retain
            // the parser ComponentRecordType.
            var names = new string[arity];
            for (int i = 0; i < arity; i++) names[i] = rec.Fields[i].Name;
            switch (prim)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                    return BuildIntRecord(arity, names, d, out del);
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                    return BuildLongRecord(arity, names, d, out del);
                case ComponentPrim.F32:
                    return BuildFloatRecord(arity, names, d, out del);
                case ComponentPrim.F64:
                    return BuildDoubleRecord(arity, names, d, out del);
                default:
                    return false;
            }
        }

        private static Dictionary<string, object?> ToDict(
            string[] names, params object[] values)
        {
            var dict = new Dictionary<string, object?>(names.Length);
            for (int i = 0; i < names.Length; i++)
                dict[names[i]] = values[i];
            return dict;
        }

        private static bool BuildIntRecord(
            int arity, string[] names, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, int, int>)((_, a, b) =>
                        d.TaskReturn(null!, ToDict(names, a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, int, int, int>)((_, a, b, c) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, int, int, int, int>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        private static bool BuildLongRecord(
            int arity, string[] names, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, long, long>)((_, a, b) =>
                        d.TaskReturn(null!, ToDict(names, a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, long, long, long>)((_, a, b, c) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, long, long, long, long>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        private static bool BuildFloatRecord(
            int arity, string[] names, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, float, float>)((_, a, b) =>
                        d.TaskReturn(null!, ToDict(names, a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, float, float, float>)((_, a, b, c) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, float, float, float, float>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        private static bool BuildDoubleRecord(
            int arity, string[] names, AsyncDispatcher d, out Delegate? del)
        {
            switch (arity)
            {
                case 2:
                    del = (Action<ExecContext, double, double>)((_, a, b) =>
                        d.TaskReturn(null!, ToDict(names, a, b)));
                    return true;
                case 3:
                    del = (Action<ExecContext, double, double, double>)((_, a, b, c) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c)));
                    return true;
                case 4:
                    del = (Action<ExecContext, double, double, double, double>)((_, a, b, c, e) =>
                        d.TaskReturn(null!, ToDict(names, a, b, c, e)));
                    return true;
            }
            del = null;
            return false;
        }

        // task.return variant: two recognized shapes.
        //   1. All-unit-cases (no case carries a payload) → core
        //      sig is a single i32 discriminant. Same delegate
        //      shape as enum; lift surfaces the disc as int and
        //      bounds-checks against case count.
        //   2. All cases carry the SAME primitive payload type
        //      (no missing-payload cases mixed in) → core sig is
        //      (i32 disc, T payload). Lift surfaces a
        //      (int disc, T payload) ValueTuple; the host inspects
        //      the discriminant to interpret the payload semantics.
        //
        // Heterogeneous-payload variants need the canon-ABI's
        // largest-payload slot reservation per spec; that's the
        // Session 3 source-gen typed-variant path. We decline
        // here rather than half-implement it.
        private static bool TryBuildTaskReturnVariant(
            ComponentVariantType variant, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            int caseCount = variant.Cases.Count;
            if (caseCount == 0) return false;

            bool allUnit = true;
            for (int i = 0; i < caseCount; i++)
            {
                if (variant.Cases[i].Payload != null) { allUnit = false; break; }
            }
            if (allUnit)
            {
                del = (Action<ExecContext, int>)((_, disc) =>
                {
                    if ((uint)disc >= (uint)caseCount)
                        throw new InvalidOperationException(
                            $"task.return variant: discriminant {disc} " +
                            $"out of range for variant with {caseCount} cases");
                    d.TaskReturn(null!, disc);
                });
                return true;
            }

            // Uniform-primitive-payload variant: every case carries
            // the same primitive payload type. (No case may be
            // unit; mixing unit + payload would violate the
            // uniform-slot lowering.)
            ComponentPrim? uniform = null;
            for (int i = 0; i < caseCount; i++)
            {
                var p = variant.Cases[i].Payload;
                if (p == null) return false;
                if (!p.Value.IsPrimitive) return false;
                if (uniform == null) uniform = p.Value.Prim;
                else if (uniform.Value != p.Value.Prim) return false;
            }
            switch (uniform!.Value)
            {
                case ComponentPrim.S32:
                case ComponentPrim.U32:
                    del = (Action<ExecContext, int, int>)((_, disc, payload) =>
                    {
                        if ((uint)disc >= (uint)caseCount)
                            throw new InvalidOperationException(
                                $"task.return variant: discriminant {disc} " +
                                $"out of range for variant with {caseCount} cases");
                        d.TaskReturn(null!, (disc, payload));
                    });
                    return true;
                case ComponentPrim.S64:
                case ComponentPrim.U64:
                    del = (Action<ExecContext, int, long>)((_, disc, payload) =>
                    {
                        if ((uint)disc >= (uint)caseCount)
                            throw new InvalidOperationException(
                                $"task.return variant: discriminant {disc} " +
                                $"out of range for variant with {caseCount} cases");
                        d.TaskReturn(null!, (disc, payload));
                    });
                    return true;
                case ComponentPrim.F32:
                    del = (Action<ExecContext, int, float>)((_, disc, payload) =>
                    {
                        if ((uint)disc >= (uint)caseCount)
                            throw new InvalidOperationException(
                                $"task.return variant: discriminant {disc} " +
                                $"out of range for variant with {caseCount} cases");
                        d.TaskReturn(null!, (disc, payload));
                    });
                    return true;
                case ComponentPrim.F64:
                    del = (Action<ExecContext, int, double>)((_, disc, payload) =>
                    {
                        if ((uint)disc >= (uint)caseCount)
                            throw new InvalidOperationException(
                                $"task.return variant: discriminant {disc} " +
                                $"out of range for variant with {caseCount} cases");
                        d.TaskReturn(null!, (disc, payload));
                    });
                    return true;
                default:
                    return false;
            }
        }

        // task.return flags ≤32: flat-lowered to a single i32
        // bitfield per canon-ABI (FlatLowering.FlatCountDef
        // returns ⌈N/32⌉, so ≤32 flags → 1). Each bit position
        // corresponds to a named flag in declaration order. >32
        // flags requires multiple i32 slots — declined here;
        // the source generator path will lift those into a CLR
        // [Flags] enum directly.
        private static bool TryBuildTaskReturnFlags(
            ComponentFlagsType fl, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            if (fl.Flags.Count > 32) return false;
            // The high bits beyond the declared count must be
            // zero; mask & validate to catch malformed guest output.
            int flagCount = fl.Flags.Count;
            uint validMask = flagCount == 32 ? 0xFFFFFFFFu
                : (1u << flagCount) - 1u;
            del = (Action<ExecContext, int>)((_, bits) =>
            {
                uint u = unchecked((uint)bits);
                if ((u & ~validMask) != 0)
                    throw new InvalidOperationException(
                        $"task.return flags: bits 0x{u:X8} have " +
                        $"reserved high bits set " +
                        $"(declared count = {flagCount})");
                d.TaskReturn(null!, u);
            });
            return true;
        }
    }
}
