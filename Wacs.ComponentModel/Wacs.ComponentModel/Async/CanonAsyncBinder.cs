// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
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
                // task.return: bind iff the resultlist is a
                // primitive valtype (or empty). Aggregate/string
                // results need the canon-ABI lift adapter and
                // remain deferred.
                case CanonTaskReturn tr:
                    if (tr.Result == null || IsBindablePrimitive(tr.Result.Value))
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
        // to dispatcher.TaskReturn(object?).
        private static bool TryBuildTaskReturn(
            CanonTaskReturn tr, AsyncDispatcher d, out Delegate? del)
        {
            del = null;
            if (tr.Result == null)
            {
                del = (Action<ExecContext>)((_) => d.TaskReturn(null!, null));
                return true;
            }
            if (!tr.Result.Value.IsPrimitive) return false;
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
    }
}
