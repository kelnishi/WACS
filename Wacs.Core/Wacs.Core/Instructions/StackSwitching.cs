// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// One on-tag handler entry inside a <c>resume</c> /
    /// <c>resume_throw</c> immediate. The proposal defines two
    /// shapes that share the same wire form (tag + label); a
    /// <see cref="OnSwitch"/> flag distinguishes them.
    /// </summary>
    public readonly struct StackSwitchHandler
    {
        public readonly TagIdx Tag;
        public readonly LabelIdx Label;
        public readonly bool OnSwitch;

        public StackSwitchHandler(TagIdx tag, LabelIdx label, bool onSwitch)
        {
            Tag = tag;
            Label = label;
            OnSwitch = onSwitch;
        }
    }

    /// <summary>
    /// <c>cont.new $ct</c> (0xE0) — wrap a function reference as a
    /// suspended continuation. Stack: <c>[(ref null $ft)] → [(ref $ct)]</c>
    /// where <c>$ct = cont $ft</c>.
    /// </summary>
    public class InstContNew : InstructionBase
    {
        public InstContNew() : base(ByteCode.ContNew) { }

        private TypeIdx X;
        public int TypeIndex => X.Value;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "cont.new: type index {0} out of range.", X);
            var defType = context.Types[X];
            var ct = defType.Expansion as ContType;
            context.Assert(ct,
                "cont.new: type {0} is not a continuation type.", X);
            // Inner function type is referenced by ct.FuncTypeRef
            // (a typeidx valtype). Resolve and check it expands to a
            // FunctionType for completeness of the proposal's typing
            // judgement.
            context.Assert(ct!.FuncTypeRef.IsDefType()
                           && context.Types.Contains(ct.FuncTypeRef.Index()),
                "cont.new: continuation type's inner function type {0} out of range.",
                ct.FuncTypeRef.Index());
            var innerFt = context.Types[ct.FuncTypeRef.Index()].Expansion as FunctionType;
            context.Assert(innerFt,
                "cont.new: continuation inner type is not a function type.");
            context.OpStack.PopType(ValType.FuncRef);
            context.OpStack.PushType(ValType.ContRefNN);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Count > 0,
                "cont.new: empty operand stack.");
            var refVal = context.OpStack.PopRefType();
            if (refVal.IsNullRef)
                throw new TrapException("cont.new: null function reference.");
            var funcAddr = refVal.GetFuncAddr(context.Frame.Module.Types);
            context.Assert(context.Store.Contains(funcAddr),
                $"cont.new: function address {funcAddr} is not in the store.");
            var contInst = context.Store.AllocateContinuation(X, funcAddr);
            context.OpStack.PushValue(new Value(ValType.ContRefNN, contInst));
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override void RenderBinary(BinaryWriter writer) =>
            writer.WriteLeb128_u32((uint)X.Value);
    }

    /// <summary>
    /// <c>cont.bind $ct1 $ct2</c> (0xE1) — partially apply arguments
    /// to a continuation. Stack:
    /// <c>[t* (ref null $ct1)] → [(ref $ct2)]</c> where
    /// <c>$ct1 = cont [t* t'*] → [r*]</c> and
    /// <c>$ct2 = cont [t'*] → [r*]</c>.
    /// </summary>
    public class InstContBind : InstructionBase
    {
        public InstContBind() : base(ByteCode.ContBind) { }

        private TypeIdx X1;
        private TypeIdx X2;
        public int TypeIndex1 => X1.Value;
        public int TypeIndex2 => X2.Value;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X1),
                "cont.bind: source type index {0} out of range.", X1);
            context.Assert(context.Types.Contains(X2),
                "cont.bind: target type index {0} out of range.", X2);
            var ct1 = context.Types[X1].Expansion as ContType;
            var ct2 = context.Types[X2].Expansion as ContType;
            context.Assert(ct1, "cont.bind: type {0} is not a continuation type.", X1);
            context.Assert(ct2, "cont.bind: type {0} is not a continuation type.", X2);
            var ft1 = context.Types[ct1!.FuncTypeRef.Index()].Expansion as FunctionType;
            var ft2 = context.Types[ct2!.FuncTypeRef.Index()].Expansion as FunctionType;
            context.Assert(ft1, "cont.bind: source inner type is not a function type.");
            context.Assert(ft2, "cont.bind: target inner type is not a function type.");
            // Per proposal: ft1.params = bind-prefix ++ ft2.params,
            // ft1.results = ft2.results. Pop bind-prefix (ft1.params
            // minus ft2.params suffix), then the source continuation
            // ref, then push target continuation ref. Structural
            // typecheck of each bind-prefix slot against ft1.params
            // is delegated to the dispatcher; this validator only
            // gates on arity.
            context.Assert(ft1!.ParameterTypes.Arity >= ft2!.ParameterTypes.Arity,
                "cont.bind: source params arity {0} must be >= target params arity {1}.",
                ft1.ParameterTypes.Arity, ft2.ParameterTypes.Arity);
            context.OpStack.PopType(ValType.ContRef);
            int bindCount = ft1.ParameterTypes.Arity - ft2.ParameterTypes.Arity;
            for (int i = 0; i < bindCount; i++)
            {
                context.OpStack.PopAny();
            }
            context.OpStack.PushType(ValType.ContRefNN);
        }

        public override void Execute(ExecContext context)
        {
            // The proposal models cont.bind as "shrink the param
            // arity by partial application" — outer source signature
            // has more params, target has fewer. The continuation
            // (source) is on top of the stack; immediately below it
            // are the bind-prefix args. Pop them all, prepend the
            // prefix to the source's existing BoundArgs, and produce
            // a new ContInstance retyped to the target.
            context.Assert(context.OpStack.Count > 0,
                "cont.bind: empty operand stack.");
            var contVal = context.OpStack.PopRefType();
            if (contVal.IsNullRef)
                throw new TrapException("cont.bind: null continuation reference.");
            var source = contVal.GcRef as ContInstance;
            context.Assert(source,
                "cont.bind: ref does not point to a continuation.");
            context.Assert(source!.State == ContState.Fresh,
                $"cont.bind: continuation is not fresh (state {source.State}).");

            var srcFt = context.Frame.Module.Types[X1].Expansion as FunctionType;
            var tgtFt = context.Frame.Module.Types[X2].Expansion as FunctionType;
            context.Assert(srcFt,
                $"cont.bind: source type {X1} is not a function type.");
            context.Assert(tgtFt,
                $"cont.bind: target type {X2} is not a function type.");
            int bindCount = srcFt!.ParameterTypes.Arity - tgtFt!.ParameterTypes.Arity;

            // Pop bind args in reverse so the leading-position
            // semantics is preserved when re-pushed at resume time.
            var prefix = new Value[bindCount];
            for (int i = bindCount - 1; i >= 0; i--)
                prefix[i] = context.OpStack.PopAny();

            var bound = context.Store.AllocateContinuation(X2, source.Function);
            bound.BoundArgs.AddRange(source.BoundArgs);
            bound.BoundArgs.AddRange(prefix);
            // The source continuation is consumed by cont.bind —
            // re-using it would let two callers race on the same
            // underlying function. Mark it spent.
            source.State = ContState.Completed;
            context.OpStack.PushValue(new Value(ValType.ContRefNN, bound));
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X1 = (TypeIdx)reader.ReadLeb128_u32();
            X2 = (TypeIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override void RenderBinary(BinaryWriter writer)
        {
            writer.WriteLeb128_u32((uint)X1.Value);
            writer.WriteLeb128_u32((uint)X2.Value);
        }
    }

    /// <summary>
    /// <c>suspend $tag</c> (0xE2) — suspend the current continuation
    /// to the nearest handler for <c>$tag</c>. Stack matches the
    /// tag's function type: <c>[t1*] → [t2*]</c>.
    /// </summary>
    public class InstSuspend : InstructionBase
    {
        public InstSuspend() : base(ByteCode.Suspend) { }

        private TagIdx X;
        public int TagIndex => (int)X.Value;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Tags.Contains(X),
                "suspend: tag {0} does not exist in the context.", X);
            var tag = context.Tags[X];
            var ft = context.Types[tag.TypeIndex].Expansion as FunctionType;
            context.Assert(ft,
                "suspend: tag {0} is not a function type.", X);
            context.OpStack.DiscardValues(ft!.ParameterTypes);
            context.OpStack.PushResult(ft.ResultType);
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.Frame.Module.TagAddrs.Contains(X),
                $"suspend: tag {X} not addressable in this frame's module.");
            var ta = context.Frame.Module.TagAddrs[X];
            context.Assert(context.Store.Contains(ta),
                $"suspend: tag address {ta} is not in the store.");
            var ti = context.Store[ta];
            var ft = (ti.Type.Expansion as FunctionType)!;

            var payload = new Stack<Value>();
            context.OpStack.PopResults(ft.ParameterTypes, ref payload);
            throw new SuspensionException(ta, payload);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TagIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override void RenderBinary(BinaryWriter writer) =>
            writer.WriteLeb128_u32(X.Value);
    }

    /// <summary>
    /// <c>resume $ct handler*</c> (0xE3) — resume a continuation,
    /// installing on-tag handlers that branch to enclosing labels
    /// on suspend. Stack: <c>[t1* (ref null $ct)] → [t2*]</c>.
    /// </summary>
    public class InstResume : InstructionBase
    {
        public InstResume() : base(ByteCode.Resume) { }

        private TypeIdx X;
        public StackSwitchHandler[] Handlers = Array.Empty<StackSwitchHandler>();
        public int TypeIndex => X.Value;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "resume: type index {0} out of range.", X);
            var ct = context.Types[X].Expansion as ContType;
            context.Assert(ct, "resume: type {0} is not a continuation type.", X);
            var ft = context.Types[ct!.FuncTypeRef.Index()].Expansion as FunctionType;
            context.Assert(ft, "resume: continuation inner type is not a function type.");

            // Existence of each handler's tag and label is checked
            // here. Arrival-arity check at the handler label (tag
            // params + continuation rest match the label's arrival
            // types) is delegated to the dispatcher.
            foreach (var h in Handlers)
            {
                context.Assert(context.Tags.Contains(h.Tag),
                    "resume: handler tag {0} does not exist in the context.", h.Tag);
                context.Assert(context.ContainsLabel(h.Label.Value),
                    "resume: handler label {0} does not exist in the context.", h.Label);
            }

            context.OpStack.PopType(ValType.ContRef);
            context.OpStack.DiscardValues(ft!.ParameterTypes);
            context.OpStack.PushResult(ft.ResultType);
        }

        public override void Execute(ExecContext context) =>
            throw new NotImplementedException("resume is not implemented.");

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Handlers = ParseHandlerVector(reader);
            return this;
        }

        public override void RenderBinary(BinaryWriter writer)
        {
            writer.WriteLeb128_u32((uint)X.Value);
            WriteHandlerVector(writer, Handlers);
        }

        internal static StackSwitchHandler[] ParseHandlerVector(BinaryReader reader)
        {
            uint count = reader.ReadLeb128_u32();
            var arr = new StackSwitchHandler[count];
            for (uint i = 0; i < count; i++)
            {
                byte flag = reader.ReadByte();
                bool onSwitch = flag switch
                {
                    0x00 => false,
                    0x01 => true,
                    _ => throw new FormatException(
                        $"Stack-switch handler flag {flag:X} unknown at offset 0x{reader.BaseStream.Position - 1:X}.")
                };
                var tag = (TagIdx)reader.ReadLeb128_u32();
                var label = (LabelIdx)reader.ReadLeb128_u32();
                arr[i] = new StackSwitchHandler(tag, label, onSwitch);
            }
            return arr;
        }

        internal static void WriteHandlerVector(BinaryWriter writer, StackSwitchHandler[] handlers)
        {
            writer.WriteLeb128_u32((uint)handlers.Length);
            foreach (var h in handlers)
            {
                writer.Write((byte)(h.OnSwitch ? 0x01 : 0x00));
                writer.WriteLeb128_u32(h.Tag.Value);
                writer.WriteLeb128_u32(h.Label.Value);
            }
        }
    }

    /// <summary>
    /// <c>resume_throw $ct $tag handler*</c> (0xE4) — resume a
    /// continuation by throwing the exception identified by
    /// <c>$tag</c> inside it. Stack:
    /// <c>[t* (ref null $ct)] → [t2*]</c> where <c>$tag</c> has
    /// params <c>[t*]</c> and the continuation yields <c>[t2*]</c>.
    /// </summary>
    public class InstResumeThrow : InstructionBase
    {
        public InstResumeThrow() : base(ByteCode.ResumeThrow) { }

        private TypeIdx X;
        private TagIdx Tag;
        public StackSwitchHandler[] Handlers = Array.Empty<StackSwitchHandler>();
        public int TypeIndex => X.Value;
        public int TagIndex => (int)Tag.Value;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "resume_throw: type index {0} out of range.", X);
            var ct = context.Types[X].Expansion as ContType;
            context.Assert(ct, "resume_throw: type {0} is not a continuation type.", X);
            var ft = context.Types[ct!.FuncTypeRef.Index()].Expansion as FunctionType;
            context.Assert(ft, "resume_throw: continuation inner type is not a function type.");

            context.Assert(context.Tags.Contains(Tag),
                "resume_throw: tag {0} does not exist in the context.", Tag);
            var tagInfo = context.Tags[Tag];
            var tagFt = context.Types[tagInfo.TypeIndex].Expansion as FunctionType;
            context.Assert(tagFt,
                "resume_throw: tag {0} is not a function type.", Tag);
            context.Assert(tagFt!.ResultType.Arity == 0,
                "resume_throw: exception tag {0} must have empty result type.", Tag);

            foreach (var h in Handlers)
            {
                context.Assert(context.Tags.Contains(h.Tag),
                    "resume_throw: handler tag {0} does not exist in the context.", h.Tag);
                context.Assert(context.ContainsLabel(h.Label.Value),
                    "resume_throw: handler label {0} does not exist in the context.", h.Label);
            }

            context.OpStack.PopType(ValType.ContRef);
            context.OpStack.DiscardValues(tagFt.ParameterTypes);
            context.OpStack.PushResult(ft!.ResultType);
        }

        public override void Execute(ExecContext context) =>
            throw new NotImplementedException("resume_throw is not implemented.");

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Tag = (TagIdx)reader.ReadLeb128_u32();
            Handlers = InstResume.ParseHandlerVector(reader);
            return this;
        }

        public override void RenderBinary(BinaryWriter writer)
        {
            writer.WriteLeb128_u32((uint)X.Value);
            writer.WriteLeb128_u32(Tag.Value);
            InstResume.WriteHandlerVector(writer, Handlers);
        }
    }

    /// <summary>
    /// <c>switch $ct $tag</c> (0xE5) — symmetric stack switch via a
    /// shared switch tag. Stack:
    /// <c>[t1* (ref null $ct)] → [t*']</c>.
    /// </summary>
    public class InstSwitch : InstructionBase
    {
        public InstSwitch() : base(ByteCode.Switch) { }

        private TypeIdx X;
        private TagIdx Tag;
        public int TypeIndex => X.Value;
        public int TagIndex => (int)Tag.Value;

        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(context.Types.Contains(X),
                "switch: type index {0} out of range.", X);
            var ct = context.Types[X].Expansion as ContType;
            context.Assert(ct, "switch: type {0} is not a continuation type.", X);
            var ft = context.Types[ct!.FuncTypeRef.Index()].Expansion as FunctionType;
            context.Assert(ft, "switch: continuation inner type is not a function type.");

            context.Assert(context.Tags.Contains(Tag),
                "switch: tag {0} does not exist in the context.", Tag);
            var tagInfo = context.Tags[Tag];
            var tagFt = context.Types[tagInfo.TypeIndex].Expansion as FunctionType;
            context.Assert(tagFt,
                "switch: tag {0} is not a function type.", Tag);
            context.Assert(tagFt!.ResultType.Arity == 0,
                "switch: switch tag {0} must have empty result type (its results " +
                "are the producer's return slots).", Tag);

            context.OpStack.PopType(ValType.ContRef);
            // The tag's parameters describe the values that flow
            // between switching peers; structural matching of those
            // against ft's params is delegated to the dispatcher.
            context.OpStack.DiscardValues(ft!.ParameterTypes);
            context.OpStack.PushResult(ft.ResultType);
        }

        public override void Execute(ExecContext context) =>
            throw new NotImplementedException("switch is not implemented.");

        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TypeIdx)reader.ReadLeb128_u32();
            Tag = (TagIdx)reader.ReadLeb128_u32();
            return this;
        }

        public override void RenderBinary(BinaryWriter writer)
        {
            writer.WriteLeb128_u32((uint)X.Value);
            writer.WriteLeb128_u32(Tag.Value);
        }
    }
}
