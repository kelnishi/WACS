// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.ComponentModel.Runtime.Parser
{
    /// <summary>
    /// A single canon-section entry — describes one of the
    /// component-level functions produced by canonicalization.
    /// <c>canon lift</c> takes a core-wasm function and produces
    /// a component-level one (wrapping it with lift code for the
    /// canonical-ABI conversions). <c>canon lower</c> goes the
    /// other way. Resource ops (<c>resource.new/drop/rep</c>)
    /// generate handle-table accessors.
    /// </summary>
    public abstract class CanonEntry { }

    /// <summary>
    /// <c>canon lift</c>: lift a core function at index
    /// <see cref="CoreFuncIdx"/> into a component function with
    /// the component type at <see cref="TypeIdx"/>. Canonical
    /// options (<see cref="Options"/>) specify memory / realloc /
    /// post-return / string-encoding. The transpiler consumes
    /// these to generate the adapter IL that converts between
    /// C#-level types (<c>string</c>, <c>byte[]</c>, …) and the
    /// flat core-wasm parameter list.
    /// </summary>
    public sealed class CanonLift : CanonEntry
    {
        public uint CoreFuncIdx { get; }
        public uint TypeIdx { get; }
        public IReadOnlyList<CanonOption> Options { get; }

        public CanonLift(uint coreFuncIdx, uint typeIdx,
                         IReadOnlyList<CanonOption> options)
        {
            CoreFuncIdx = coreFuncIdx;
            TypeIdx = typeIdx;
            Options = options;
        }
    }

    /// <summary>
    /// <c>canon lower</c>: lower a component function at index
    /// <see cref="FuncIdx"/> into a core-wasm function. Used
    /// for imports — the component calls the host via the
    /// lowered core function.
    /// </summary>
    public sealed class CanonLower : CanonEntry
    {
        public uint FuncIdx { get; }
        public IReadOnlyList<CanonOption> Options { get; }

        public CanonLower(uint funcIdx, IReadOnlyList<CanonOption> options)
        {
            FuncIdx = funcIdx;
            Options = options;
        }
    }

    /// <summary>
    /// <c>canon resource.new</c> / <c>resource.drop</c> /
    /// <c>resource.rep</c> — handle-table intrinsics generated
    /// for a particular resource type.
    /// </summary>
    public sealed class CanonResourceOp : CanonEntry
    {
        public enum Kind { New, Drop, Rep }
        public Kind Op { get; }
        public uint ResourceTypeIdx { get; }

        public CanonResourceOp(Kind op, uint resourceTypeIdx)
        {
            Op = op;
            ResourceTypeIdx = resourceTypeIdx;
        }
    }

    // ---- Async ABI canon builtins -----------------------------------------
    //
    // Phase 2 of WASIp3 work: parse the async-ABI canon builtin
    // family. Data plane / dispatcher are Phase 3 — these entries
    // are pure shape (typeidx / opts / async? / cancel?) capture.

    /// <summary><c>canon task.return rs opts</c> (0x09). Returns from an
    /// async task with the lift-marshaled result list. <see cref="Result"/>
    /// is <c>null</c> for an empty resultlist (<c>(result)</c>).</summary>
    public sealed class CanonTaskReturn : CanonEntry
    {
        public ComponentValType? Result { get; }
        public IReadOnlyList<CanonOption> Options { get; }
        public CanonTaskReturn(ComponentValType? result, IReadOnlyList<CanonOption> options)
        {
            Result = result;
            Options = options;
        }
    }

    /// <summary><c>canon task.cancel</c> (0x05). Marker — no payload.</summary>
    public sealed class CanonTaskCancel : CanonEntry { }

    /// <summary><c>canon subtask.cancel async?</c> (0x06).</summary>
    public sealed class CanonSubtaskCancel : CanonEntry
    {
        public bool Async { get; }
        public CanonSubtaskCancel(bool async) { Async = async; }
    }

    /// <summary><c>canon subtask.drop</c> (0x0d). Marker.</summary>
    public sealed class CanonSubtaskDrop : CanonEntry { }

    /// <summary><c>canon backpressure.{set,inc,dec}</c> (0x08 / 0x24 / 0x25).</summary>
    public sealed class CanonBackpressureOp : CanonEntry
    {
        public enum Kind { Set, Inc, Dec }
        public Kind Op { get; }
        public CanonBackpressureOp(Kind op) { Op = op; }
    }

    /// <summary>
    /// <c>canon context.{get,set} v i</c> (0x0a / 0x0b). Reads/writes a
    /// per-task context slot of value type <see cref="ValType"/> at index
    /// <see cref="Index"/>.
    /// </summary>
    public sealed class CanonContextOp : CanonEntry
    {
        public enum Kind { Get, Set }
        public Kind Op { get; }
        public ComponentValType ValType { get; }
        public uint Index { get; }
        public CanonContextOp(Kind op, ComponentValType valType, uint index)
        {
            Op = op;
            ValType = valType;
            Index = index;
        }
    }

    /// <summary>
    /// <c>canon thread.yield cancel?</c> (0x0c). Yields the current task;
    /// when <see cref="Cancellable"/>, surfaces task-cancellation across
    /// the yield point.
    /// </summary>
    public sealed class CanonThreadYield : CanonEntry
    {
        public bool Cancellable { get; }
        public CanonThreadYield(bool cancellable) { Cancellable = cancellable; }
    }

    /// <summary>
    /// <c>canon stream.*</c> family (0x0e–0x14). All ops carry a stream
    /// type idx; read/write additionally carry <see cref="Options"/>;
    /// cancel-read/cancel-write additionally carry <see cref="Async"/>.
    /// </summary>
    public sealed class CanonStreamOp : CanonEntry
    {
        public enum Kind
        {
            New,            // 0x0e t
            Read,           // 0x0f t opts
            Write,          // 0x10 t opts
            CancelRead,     // 0x11 t async?
            CancelWrite,    // 0x12 t async?
            DropReadable,   // 0x13 t
            DropWritable,   // 0x14 t
        }
        public Kind Op { get; }
        public uint StreamTypeIdx { get; }
        public IReadOnlyList<CanonOption>? Options { get; }
        public bool? Async { get; }
        public CanonStreamOp(
            Kind op, uint typeIdx,
            IReadOnlyList<CanonOption>? options = null,
            bool? async = null)
        {
            Op = op;
            StreamTypeIdx = typeIdx;
            Options = options;
            Async = async;
        }
    }

    /// <summary>
    /// <c>canon future.*</c> family (0x15–0x1b). Same shape as
    /// <see cref="CanonStreamOp"/>.
    /// </summary>
    public sealed class CanonFutureOp : CanonEntry
    {
        public enum Kind
        {
            New,            // 0x15 t
            Read,           // 0x16 t opts
            Write,          // 0x17 t opts
            CancelRead,     // 0x18 t async?
            CancelWrite,    // 0x19 t async?
            DropReadable,   // 0x1a t
            DropWritable,   // 0x1b t
        }
        public Kind Op { get; }
        public uint FutureTypeIdx { get; }
        public IReadOnlyList<CanonOption>? Options { get; }
        public bool? Async { get; }
        public CanonFutureOp(
            Kind op, uint typeIdx,
            IReadOnlyList<CanonOption>? options = null,
            bool? async = null)
        {
            Op = op;
            FutureTypeIdx = typeIdx;
            Options = options;
            Async = async;
        }
    }

    /// <summary>
    /// <c>canon error-context.{new,debug-message,drop}</c> (0x1c–0x1e).
    /// </summary>
    public sealed class CanonErrorContextOp : CanonEntry
    {
        public enum Kind { New, DebugMessage, Drop }
        public Kind Op { get; }
        public IReadOnlyList<CanonOption>? Options { get; }
        public CanonErrorContextOp(Kind op, IReadOnlyList<CanonOption>? options = null)
        {
            Op = op;
            Options = options;
        }
    }

    /// <summary>
    /// <c>canon waitable-set.{new,wait,poll,drop}</c> (0x1f–0x22). Wait /
    /// poll carry <see cref="Cancellable"/> and a core memory index for
    /// the dispatch-result write target.
    /// </summary>
    public sealed class CanonWaitableSetOp : CanonEntry
    {
        public enum Kind { New, Wait, Poll, Drop }
        public Kind Op { get; }
        public bool? Cancellable { get; }
        public uint? MemoryIdx { get; }
        public CanonWaitableSetOp(
            Kind op, bool? cancellable = null, uint? memoryIdx = null)
        {
            Op = op;
            Cancellable = cancellable;
            MemoryIdx = memoryIdx;
        }
    }

    /// <summary><c>canon waitable.join</c> (0x23). Marker.</summary>
    public sealed class CanonWaitableJoin : CanonEntry { }

    /// <summary>
    /// A canonical-ABI option — controls how a lift / lower
    /// performs the conversion. Several are just tag bytes with
    /// no payload (<c>string-encoding</c> variants,
    /// <c>async</c>); others carry an index into the relevant
    /// core-wasm sort (<c>memory</c>, <c>realloc</c>,
    /// <c>post-return</c>, <c>callback</c>).
    /// </summary>
    public sealed class CanonOption
    {
        public enum Kind : byte
        {
            StringUtf8 = 0x00,
            StringUtf16 = 0x01,
            StringLatin1OrUtf16 = 0x02,
            Memory = 0x03,
            Realloc = 0x04,
            PostReturn = 0x05,
            Async = 0x06,
            Callback = 0x07,
        }

        public Kind OptionKind { get; }
        /// <summary>Non-null for memory / realloc / post-return
        /// / callback — the index into the relevant core-wasm
        /// sort (memory idx or func idx).</summary>
        public uint? Index { get; }

        public CanonOption(Kind kind, uint? index)
        {
            OptionKind = kind;
            Index = index;
        }
    }

    /// <summary>
    /// Decodes the component <c>canon</c> section
    /// (<see cref="ComponentSectionId.Canon"/> = 0x08). One
    /// entry per canonicalization — every lift adds to the
    /// component function space, every lower adds to the core
    /// function space, every resource op adds a handle-table
    /// intrinsic.
    ///
    /// <para>Canon opcode byte assignments (verified against
    /// WebAssembly/component-model main HEAD,
    /// design/mvp/Binary.md):</para>
    /// <code>
    /// 0x00 0x00 lift                    f opts t
    /// 0x01 0x00 lower                   f opts
    /// 0x02      resource.new            t
    /// 0x03      resource.drop           t
    /// 0x04      resource.rep            t
    /// 0x05      task.cancel             —
    /// 0x06      subtask.cancel          async?
    /// 0x08      backpressure.set        —
    /// 0x09      task.return             resultlist opts
    /// 0x0a      context.get             v i
    /// 0x0b      context.set             v i
    /// 0x0c      thread.yield            cancel?
    /// 0x0d      subtask.drop            —
    /// 0x0e      stream.new              t
    /// 0x0f      stream.read             t opts
    /// 0x10      stream.write            t opts
    /// 0x11      stream.cancel-read      t async?
    /// 0x12      stream.cancel-write     t async?
    /// 0x13      stream.drop-readable    t
    /// 0x14      stream.drop-writable    t
    /// 0x15      future.new              t
    /// 0x16      future.read             t opts
    /// 0x17      future.write            t opts
    /// 0x18      future.cancel-read      t async?
    /// 0x19      future.cancel-write     t async?
    /// 0x1a      future.drop-readable    t
    /// 0x1b      future.drop-writable    t
    /// 0x1c      error-context.new       opts
    /// 0x1d      error-context.debug-msg opts
    /// 0x1e      error-context.drop      —
    /// 0x1f      waitable-set.new        —
    /// 0x20      waitable-set.wait       cancel? memidx
    /// 0x21      waitable-set.poll       cancel? memidx
    /// 0x22      waitable-set.drop       —
    /// 0x23      waitable.join           —
    /// 0x24      backpressure.inc        —
    /// 0x25      backpressure.dec        —
    /// 0x26-0x2b thread.*                (omitted — Phase 1
    ///                                    Stack Switching domain)
    /// 0x40-0x42 thread.spawn-*          (deferred — explicit
    ///                                    threads proposal)
    /// </code>
    /// <para>The async? / cancel? / sh? sub-byte productions
    /// all share the same shape: 0x00 = absent, 0x01 = present
    /// (with payload if any).</para>
    /// </summary>
    public static class CanonSectionReader
    {
        public static List<CanonEntry> Decode(byte[] payload)
        {
            var reader = new ComponentBinaryReader(payload);
            var count = reader.ReadVarU32();
            var entries = new List<CanonEntry>((int)count);
            for (uint i = 0; i < count; i++)
                entries.Add(DecodeEntry(reader));
            return entries;
        }

        private static CanonEntry DecodeEntry(ComponentBinaryReader r)
        {
            var opcode = r.ReadByte();
            switch (opcode)
            {
                case 0x00:
                {
                    var sub = r.ReadByte();
                    if (sub != 0x00)
                        throw new FormatException(
                            $"Unexpected canon-lift sub-opcode 0x{sub:X2}.");
                    var f = r.ReadVarU32();
                    var opts = DecodeOptions(r);
                    var t = r.ReadVarU32();
                    return new CanonLift(f, t, opts);
                }
                case 0x01:
                {
                    var sub = r.ReadByte();
                    if (sub != 0x00)
                        throw new FormatException(
                            $"Unexpected canon-lower sub-opcode 0x{sub:X2}.");
                    var f = r.ReadVarU32();
                    var opts = DecodeOptions(r);
                    return new CanonLower(f, opts);
                }
                case 0x02:
                    return new CanonResourceOp(
                        CanonResourceOp.Kind.New, r.ReadVarU32());
                case 0x03:
                    return new CanonResourceOp(
                        CanonResourceOp.Kind.Drop, r.ReadVarU32());
                case 0x04:
                    return new CanonResourceOp(
                        CanonResourceOp.Kind.Rep, r.ReadVarU32());

                // ---- Async ABI canon builtins (0x05–0x25) -------------
                case 0x05: return new CanonTaskCancel();
                case 0x06: return new CanonSubtaskCancel(DecodePresence(r));
                case 0x08:
                    return new CanonBackpressureOp(CanonBackpressureOp.Kind.Set);
                case 0x09:
                {
                    var rs = DecodeResultList(r);
                    var opts = DecodeOptions(r);
                    return new CanonTaskReturn(rs, opts);
                }
                case 0x0a:
                {
                    var v = DecodeValType(r);
                    var i = r.ReadVarU32();
                    return new CanonContextOp(CanonContextOp.Kind.Get, v, i);
                }
                case 0x0b:
                {
                    var v = DecodeValType(r);
                    var i = r.ReadVarU32();
                    return new CanonContextOp(CanonContextOp.Kind.Set, v, i);
                }
                case 0x0c: return new CanonThreadYield(DecodePresence(r));
                case 0x0d: return new CanonSubtaskDrop();

                case 0x0e: return new CanonStreamOp(CanonStreamOp.Kind.New, r.ReadVarU32());
                case 0x0f:
                {
                    var t = r.ReadVarU32(); var o = DecodeOptions(r);
                    return new CanonStreamOp(CanonStreamOp.Kind.Read, t, options: o);
                }
                case 0x10:
                {
                    var t = r.ReadVarU32(); var o = DecodeOptions(r);
                    return new CanonStreamOp(CanonStreamOp.Kind.Write, t, options: o);
                }
                case 0x11:
                {
                    var t = r.ReadVarU32(); var a = DecodePresence(r);
                    return new CanonStreamOp(CanonStreamOp.Kind.CancelRead, t, async: a);
                }
                case 0x12:
                {
                    var t = r.ReadVarU32(); var a = DecodePresence(r);
                    return new CanonStreamOp(CanonStreamOp.Kind.CancelWrite, t, async: a);
                }
                case 0x13: return new CanonStreamOp(CanonStreamOp.Kind.DropReadable, r.ReadVarU32());
                case 0x14: return new CanonStreamOp(CanonStreamOp.Kind.DropWritable, r.ReadVarU32());

                case 0x15: return new CanonFutureOp(CanonFutureOp.Kind.New, r.ReadVarU32());
                case 0x16:
                {
                    var t = r.ReadVarU32(); var o = DecodeOptions(r);
                    return new CanonFutureOp(CanonFutureOp.Kind.Read, t, options: o);
                }
                case 0x17:
                {
                    var t = r.ReadVarU32(); var o = DecodeOptions(r);
                    return new CanonFutureOp(CanonFutureOp.Kind.Write, t, options: o);
                }
                case 0x18:
                {
                    var t = r.ReadVarU32(); var a = DecodePresence(r);
                    return new CanonFutureOp(CanonFutureOp.Kind.CancelRead, t, async: a);
                }
                case 0x19:
                {
                    var t = r.ReadVarU32(); var a = DecodePresence(r);
                    return new CanonFutureOp(CanonFutureOp.Kind.CancelWrite, t, async: a);
                }
                case 0x1a: return new CanonFutureOp(CanonFutureOp.Kind.DropReadable, r.ReadVarU32());
                case 0x1b: return new CanonFutureOp(CanonFutureOp.Kind.DropWritable, r.ReadVarU32());

                case 0x1c:
                    return new CanonErrorContextOp(
                        CanonErrorContextOp.Kind.New, DecodeOptions(r));
                case 0x1d:
                    return new CanonErrorContextOp(
                        CanonErrorContextOp.Kind.DebugMessage, DecodeOptions(r));
                case 0x1e: return new CanonErrorContextOp(CanonErrorContextOp.Kind.Drop);

                case 0x1f: return new CanonWaitableSetOp(CanonWaitableSetOp.Kind.New);
                case 0x20:
                {
                    var c = DecodePresence(r); var m = r.ReadVarU32();
                    return new CanonWaitableSetOp(
                        CanonWaitableSetOp.Kind.Wait, cancellable: c, memoryIdx: m);
                }
                case 0x21:
                {
                    var c = DecodePresence(r); var m = r.ReadVarU32();
                    return new CanonWaitableSetOp(
                        CanonWaitableSetOp.Kind.Poll, cancellable: c, memoryIdx: m);
                }
                case 0x22: return new CanonWaitableSetOp(CanonWaitableSetOp.Kind.Drop);
                case 0x23: return new CanonWaitableJoin();
                case 0x24: return new CanonBackpressureOp(CanonBackpressureOp.Kind.Inc);
                case 0x25: return new CanonBackpressureOp(CanonBackpressureOp.Kind.Dec);

                default:
                    throw new FormatException(
                        $"Unsupported canon opcode 0x{opcode:X2}. "
                        + "Threading intrinsics (0x26–0x2b / 0x40–0x42) "
                        + "are deferred to the explicit-threads phase.");
            }
        }

        // Shared sub-byte presence helper for async? / cancel? / sh?.
        // 0x00 = absent, 0x01 = present (no payload).
        private static bool DecodePresence(ComponentBinaryReader r)
        {
            var b = r.ReadByte();
            if (b == 0x00) return false;
            if (b == 0x01) return true;
            throw new FormatException(
                $"Expected async?/cancel? presence byte (0x00 or 0x01), got 0x{b:X2}.");
        }

        // resultlist ::= 0x00 t:valtype       => (result t)
        //              | 0x01 0x00            => empty
        private static ComponentValType? DecodeResultList(ComponentBinaryReader r)
        {
            var prefix = r.ReadByte();
            if (prefix == 0x00) return DecodeValType(r);
            if (prefix == 0x01)
            {
                var term = r.ReadByte();
                if (term != 0x00)
                    throw new FormatException(
                        $"Expected resultlist empty-terminator 0x00, got 0x{term:X2}.");
                return null;
            }
            throw new FormatException(
                $"Expected resultlist prefix byte (0x00 or 0x01), got 0x{prefix:X2}.");
        }

        // valtype is signed-LEB i33: negative = primitive, non-neg = typeidx.
        private static ComponentValType DecodeValType(ComponentBinaryReader r)
        {
            var v = r.ReadVarI33();
            return v < 0
                ? ComponentValType.OfPrim((ComponentPrim)v)
                : ComponentValType.OfRef((uint)v);
        }

        private static IReadOnlyList<CanonOption> DecodeOptions(
            ComponentBinaryReader r)
        {
            var count = r.ReadVarU32();
            if (count == 0) return System.Array.Empty<CanonOption>();
            var opts = new CanonOption[count];
            for (uint i = 0; i < count; i++)
                opts[i] = DecodeOption(r);
            return opts;
        }

        private static CanonOption DecodeOption(ComponentBinaryReader r)
        {
            var tag = (CanonOption.Kind)r.ReadByte();
            uint? idx = null;
            switch (tag)
            {
                case CanonOption.Kind.Memory:
                case CanonOption.Kind.Realloc:
                case CanonOption.Kind.PostReturn:
                case CanonOption.Kind.Callback:
                    idx = r.ReadVarU32();
                    break;
                case CanonOption.Kind.StringUtf8:
                case CanonOption.Kind.StringUtf16:
                case CanonOption.Kind.StringLatin1OrUtf16:
                case CanonOption.Kind.Async:
                    break;
                default:
                    throw new FormatException(
                        $"Unsupported canon option tag 0x{(byte)tag:X2}.");
            }
            return new CanonOption(tag, idx);
        }
    }
}
