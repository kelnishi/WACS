// Copyright 2024 Kelvin Nishikawa
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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.ObjectPool;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using InstructionPointer = System.Int32;

namespace Wacs.Core.Runtime
{
    public struct ExecStat
    {
        public long duration;
        public long count;
    }

    public class ExecContext
    {
        // public int _sequenceCount;

        public const int AbortSequence = -2;
        public static readonly Frame NullFrame = new();

        private static readonly ValType[] EmptyLocals = Array.Empty<ValType>();
        private readonly Stack<Frame> _callStack;
        private readonly ObjectPool<Frame> _framePool;

        private readonly Stack<BlockTarget> _linkLabelStack = new();
        private readonly ArrayPool<Value> _localsDataPool;
        public readonly Stopwatch InstructionTimer = new();

        public readonly OpStack OpStack;

        public readonly Stopwatch ProcessTimer = new();

        /// <summary>
        /// Shared runtime state owned by the <see cref="WasmRuntime"/> and referenced
        /// by every <see cref="ExecContext"/> bound to it. Contains the <see cref="Store"/>,
        /// <see cref="RuntimeAttributes"/>, and linked-instruction arrays — all treated as
        /// read-only post-instantiation so multiple per-thread ExecContexts (Layer 1c)
        /// can share it safely without locks.
        /// </summary>
        internal readonly SharedRuntimeState Shared;

        public Store Store => Shared.Store;
        public RuntimeAttributes Attributes => Shared.Attributes;
        public InstructionSequence linkedInstructions => Shared.LinkedInstructions;

        /// <summary>
        /// Hot-path dispatch reads this property; the JIT inlines the Shared-field
        /// delegation into a single load. Set once by <see cref="CacheInstructions"/>
        /// during instantiation.
        /// </summary>
        public InstructionBase[] _currentSequence
        {
            get => Shared.CurrentSequence!;
            set => Shared.CurrentSequence = value;
        }
        // public List<InstructionBase> _sequenceInstructions;


        public Frame Frame = NullFrame;
        public int InstructionPointer;

        /// <summary>
        /// Optional cancellation signal observed at function-call boundaries
        /// (Layer 1f). When set and cancelled, the invoke path throws
        /// <see cref="InterruptedException"/> with <see cref="InterruptReason"/>
        /// so the trap propagates through to <c>WasmThread.Completion</c>.
        ///
        /// <para>Set by <see cref="ThreadBasedHost"/> on the per-thread ExecContext
        /// before dispatching the entry point. Default <see cref="CancellationToken.None"/>
        /// — single-threaded callers pay no observation cost.</para>
        /// </summary>
        public CancellationToken Ct;

        /// <summary>
        /// Reason string stamped by the cancellation source (e.g. from
        /// <see cref="WasmThread.RequestTrap"/>). Reported through the thrown
        /// <see cref="InterruptedException"/>.
        /// </summary>
        public string InterruptReason = "cancelled";
        public int LinkOpStackHeight;
        public int MaxLinkOpStackHeight;
        public bool LinkUnreachable;
        public int LinkLocalCount;
        public readonly Dictionary<Value, int> LinkConstants = new();

        public Dictionary<ushort, ExecStat> Stats = new();
        public long steps;

        /// <summary>
        /// Per-thread storage for thread-local globals (Layer 5c). Each
        /// <see cref="ExecContext"/> is per-host-thread (Layer 1c), so
        /// keying thread-local global slots off the context gives us
        /// natural per-thread scoping without a cross-thread dictionary.
        /// Null until a thread-local global is first accessed on this
        /// thread; thereafter, lazy-initialized from the module's declared
        /// initializer (<see cref="Types.GlobalInstance.Value"/>) on first
        /// touch per-global per-thread.
        /// </summary>
        private Dictionary<GlobalAddr, Value>? _threadLocalGlobals;

        /// <summary>Read a thread-local global's current value for this
        /// host thread. On first access, initializes from the supplied
        /// initializer (the module's declared initial value).</summary>
        public Value GetThreadLocalGlobalValue(GlobalAddr addr, Value initialValue)
        {
            _threadLocalGlobals ??= new Dictionary<GlobalAddr, Value>();
            if (!_threadLocalGlobals.TryGetValue(addr, out var v))
            {
                v = initialValue;
                _threadLocalGlobals[addr] = v;
            }
            return v;
        }

        /// <summary>Write a thread-local global's value for this host
        /// thread; other threads' slots are unaffected.</summary>
        public void SetThreadLocalGlobalValue(GlobalAddr addr, Value value)
        {
            _threadLocalGlobals ??= new Dictionary<GlobalAddr, Value>();
            _threadLocalGlobals[addr] = value;
        }

        /// <summary>
        /// Legacy — unused after phase M. Was the native-recursion depth counter
        /// for the old switch-runtime model where every WASM <c>call</c> grew the
        /// native thread stack. Phase M replaced native recursion with
        /// <see cref="_switchCallStack"/>; depth is now that stack's <c>Count</c>.
        /// Retained so older callers still compile.
        /// </summary>
        public int SwitchCallDepth;

        /// <summary>
        /// Legacy — unused after phase M. Was the cross-Run tail-call handoff for
        /// <c>return_call</c> / <c>return_call_ref</c> / <c>return_call_indirect</c>
        /// when each nested Run invocation was a separate native frame. Phase M's
        /// iterative dispatcher handles tail calls inline in the opcode case body
        /// (release current frame in place, switch locals to the callee, don't push
        /// a new <see cref="Compilation.SwitchCallFrame"/>). Retained so older
        /// callers still compile.
        /// </summary>
        public Types.FunctionInstance? TailCallPending;

        /// <summary>
        /// Switch-runtime pc state, moved onto ExecContext so
        /// <c>GeneratedDispatcher.Run</c> doesn't need <c>ref int pc / ref int pcBefore</c>
        /// parameters — those refs alias-pinned the method's local pc to a stack slot
        /// and prevented RyuJIT from register-allocating it across the hot dispatch loop.
        /// With the fields here, Run hoists them into plain method locals at entry and
        /// writes back on exit (via try/finally, so even exceptional exits leave
        /// SwitchRuntime's handler-resume path a correct pc to re-enter at).
        ///
        /// <para>The polymorphic path doesn't touch these.</para>
        /// </summary>
        public int SwitchPc;
        public int SwitchPcBefore;

        /// <summary>
        /// Per-thread explicit call stack for the iterative switch runtime.
        ///
        /// <para>In the iterative dispatch model (phase M), every WASM <c>call</c>
        /// pushes a <see cref="Compilation.SwitchCallFrame"/> recording the caller's
        /// bytecode, handler table, pc, and Frame, then mutates the dispatch locals
        /// (<c>code</c>, <c>_codeBase</c>, <c>handlers</c>, <c>ctx.Frame</c>,
        /// <c>_localsSpan</c>, <c>_pc</c>) to point at the callee. Execution stays
        /// in the same <c>GeneratedDispatcher.Run</c> invocation — no native-stack
        /// growth per WASM call. On function exit the frame is popped and the
        /// caller's state is restored.</para>
        ///
        /// <para>Distinct from <see cref="_callStack"/> (the polymorphic path's
        /// <see cref="Types.Frame"/> stack) because the switch runtime's per-call
        /// state is a small struct, not a full Frame. Both paths are mutually
        /// exclusive at runtime (toggled by <see cref="WasmRuntime.UseSwitchRuntime"/>)
        /// so the two stacks don't interleave.</para>
        /// </summary>
        internal System.Collections.Generic.Stack<Compilation.SwitchCallFrame> _switchCallStack;

        /// <summary>
        /// Legacy constructor — builds a fresh <see cref="SharedRuntimeState"/> from
        /// the given store/attributes. Used by callers that haven't been migrated to
        /// hold a SharedRuntimeState directly.
        /// </summary>
        public ExecContext(Store store, RuntimeAttributes? attributes = default)
            : this(new SharedRuntimeState(store, attributes ?? new RuntimeAttributes()))
        {
        }

        /// <summary>
        /// Primary constructor — per-thread <see cref="ExecContext"/> instances
        /// (Layer 1c) will call this with a SharedRuntimeState owned by the runtime
        /// so each thread has its own operand stack, frame pool, locals pool, and
        /// call stack while sharing the Store, Attributes, and linked-instruction
        /// arrays.
        /// </summary>
        public ExecContext(SharedRuntimeState shared)
        {
            Shared = shared;

            _framePool = new DefaultObjectPool<Frame>(new StackPoolPolicy<Frame>(), Attributes.MaxCallStack)
                .Prime(Attributes.InitialCallStack);

            _localsDataPool = ArrayPool<Value>.Create(Attributes.MaxFunctionLocals, Attributes.LocalPoolSize);

            _callStack = new (Attributes.InitialCallStack);
            _switchCallStack = new System.Collections.Generic.Stack<Compilation.SwitchCallFrame>(Attributes.InitialCallStack);

            InstructionPointer = -1;

            OpStack = new(Attributes.MaxOpStack);
        }

        public int StackHeight => _callStack.Count;

        public InstructionBaseFactory InstructionFactory => Attributes.InstructionFactory;

        public Concurrency.IConcurrencyPolicy ConcurrencyPolicy => Attributes.ConcurrencyPolicy;

        public MemoryInstance DefaultMemory => Store[Frame.Module.MemAddrs[default]];

        public int LabelHeight => _linkLabelStack.Count;

        public void CacheInstructions()
        {
            _currentSequence = linkedInstructions._instructions.ToArray();
        }

        public void PrintInstruction(int i)
        {
            var inst = linkedInstructions[i];
            if (inst is BlockTarget label)
            {
                Console.Error.WriteLine($"[0x{i:x8}] {label.Label.StackHeight} ({inst.StackDiff:+####;-####;0}) {inst}");
            }
            else if (inst is InstEnd)
            {
                Console.Error.WriteLine($"[0x{i:x8}]  {inst.StackDiff:+####;-####;0} {inst}");
            }
            else
            {
                Console.Error.WriteLine($"[0x{i:x8}]     {inst.StackDiff:+####;-####;0} {inst}");
            }
        }

        public InstructionPointer GetPointer()
        {
            return InstructionPointer;
        }

        [Conditional("STRICT_EXECUTION")]
        public void Assert([NotNull] object? objIsNotNull, string message)
        {
            if (objIsNotNull == null)
                throw new TrapException(message);
        }

        [Conditional("STRICT_EXECUTION")]
        public void Assert(bool assertion, string message)
        {
            if (!assertion)
                throw new TrapException(message);
        }

        public ValType StackTopTopType() => 
            OpStack.Peek().Type.TopHeapType(Frame.Module.Types);

        public Frame ReserveFrame(ModuleInstance module, int arity)
        {
            var frame = _framePool.Get();
            frame.Module = module;
            frame.ReturnLabel.ContinuationAddress = InstructionPointer;
            frame.ReturnLabel.Arity = arity;
            frame.ReturnLabel.StackHeight = OpStack.Count;
            return frame;
        }

        /// <summary>
        /// Rent a bare pooled Frame without the ReserveFrame bookkeeping (return-label setup,
        /// pointer tracking). Used by the switch runtime's call path which manages its own
        /// frame lifecycle outside the polymorphic call stack.
        /// </summary>
        internal Frame RentFrame() => _framePool.Get();

        /// <summary>Return a Frame rented via <see cref="RentFrame"/> back to the pool.</summary>
        internal void ReturnFrame(Frame frame)
        {
            frame.Clear();
            _framePool.Return(frame);
        }

        public void PushFrame(Frame frame)
        {
            if (_callStack.Count >= Attributes.MaxCallStack)
                throw new WasmRuntimeException($"Runtime call stack exhausted {_callStack.Count}");

            Frame = frame;
            _callStack.Push(frame);
        }

        public InstructionPointer PopFrame()
        {
            var frame = _callStack.Pop();
#if STRICT_EXECUTION
            Assert( OpStack.Count >= frame.Arity + frame.StackHeight,
                $"Instruction return failed. Operand stack underflow");
#endif
            int resultCount = frame.ReturnLabel.Arity;
            int resultsHeight = frame.ReturnLabel.StackHeight + resultCount - frame.Locals.Length;
            OpStack.ShiftResults(resultCount, resultsHeight);
            frame.Locals = null;
            Frame = _callStack.Count > 0 ? _callStack.Peek() : NullFrame;
            
            var address = frame.ReturnLabel.ContinuationAddress;
            _framePool.Return(frame);
            return address;
        }

        public void ClearCallStack()
        {
            for (int i = _callStack.Count; i > 0; --i)
            {
                var frame = _callStack.Pop();
                frame.Locals = null;
                _framePool.Return(frame);
            }
        }

        public Frame ReuseFrame()
        {
            return _callStack.Peek();
        }

        public void TailCall(FuncAddr addr)
        {
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            var funcInst = Store[addr];
            
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    wasmFunc.TailInvoke(this);
                    return;
                // case HostFunction hostFunc:
                // {
                //     if (funcInst.IsAsync)
                //         throw new WasmRuntimeException("Cannot call asynchronous function synchronously");
                //     
                //     hostFunc.TailInvoke(this);
                //     return;
                // }
            }
            throw new WasmRuntimeException($"Unexpected function {funcInst} at address {addr}");
        }

        public BlockTarget? FindLabel(int depth)
        {
            var instructions = _currentSequence;
            int ptr = InstructionPointer;
            BlockTarget? label = null;
            
            while (ptr > Frame.Head && label == null)
            {
                var inst = instructions[--ptr];
                switch (inst)
                {
                    case BlockTarget target: 
                        label = target;
                        break;
                    case InstEnd:
                        depth += 1;
                        break;
                }
            }

            if (label is null && ptr == Frame.Head)
                return null;
            
            for (int i = 0; i < depth && label != null; i++)
            {
                label = label?.EnclosingBlock ?? null;
            }

            return label;
        }

        public void FlushCallStack()
        {
            ClearCallStack();
            OpStack.Clear();

            Frame = NullFrame;
            InstructionPointer = -1;
        }

        public void ClearLinkLabels() => _linkLabelStack.Clear();

        public void PushLabel(BlockTarget inst) => _linkLabelStack.Push(inst);

        public void DeltaStack(int stackDiff, int maxPush = 1)
        {
            LinkOpStackHeight += stackDiff;
            int maxStack = LinkOpStackHeight + maxPush;
            if (maxStack > MaxLinkOpStackHeight)
                MaxLinkOpStackHeight = maxStack;
        }

        public BlockTarget PopLabel() => _linkLabelStack.Pop();

        public BlockTarget PeekLabel() => _linkLabelStack.Peek();


        /// <summary>
        /// Observes the cancellation signal set by
        /// <see cref="ThreadBasedHost"/> / <see cref="WasmThread.RequestTrap"/>.
        /// Called at function-call boundaries (Layer 1f); <see cref="Ct"/> defaults
        /// to <see cref="CancellationToken.None"/> for callers that don't opt in,
        /// so the observation is a single cheap field read + branch.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CheckInterrupt()
        {
            if (Ct.IsCancellationRequested)
                throw new InterruptedException(InterruptReason);
        }

        // @Spec 4.4.10.1 Function Invocation
        public async Task InvokeAsync(FuncAddr addr)
        {
            CheckInterrupt();

            //1.
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            
            //2.
            var funcInst = Store[addr];
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    wasmFunc.Invoke(this);
                    return;
                case HostFunction hostFunc:
                {
                    if (hostFunc.IsAsync)
                        await hostFunc.InvokeAsync(this);
                    else
                        hostFunc.Invoke(this);
                } return;
                default:
                    funcInst.Invoke(this);
                    return;
            }
        }

        public void Invoke(FuncAddr addr)
        {
            CheckInterrupt();

            //1.
            Assert( Store.Contains(addr),
                $"Failure in Function Invocation. Address does not exist {addr}");
            
            //2.
            var funcInst = Store[addr];
            
            switch (funcInst)
            {
                case FunctionInstance wasmFunc:
                    wasmFunc.Invoke(this);
                    return;
                case HostFunction hostFunc:
                {
                    if (funcInst.IsAsync)
                        throw new WasmRuntimeException("Cannot call asynchronous function synchronously");

                    hostFunc.Invoke(this);
                } return;
                default:
                    // Generic IFunctionInstance fallback — used by TranspiledFunction
                    // and any future IFunctionInstance implementations.
                    // Pops params from OpStack, invokes, pushes results.
                    funcInst.Invoke(this);
                    return;
            }
        }

        // @Spec 4.4.10.2. Returning from a function
        public void FunctionReturn()
        {
#if STRICT_EXECUTION
            Assert( OpStack.Count >= Frame.ReturnLabel.Arity,
                $"Function Return failed. Stack did not contain return values");
#endif
            var address = PopFrame();
            InstructionPointer = address;
        }

        public List<(string, int)> ComputePointerPath()
        {
            Stack<(string, int)> ascent = new();
            int idx = InstructionPointer;
            
            // foreach (var label in Frame.EnumerateLabels().Select(target => target.Label))
            // {
            //     var pointer = (label.Instruction.GetMnemonic(), idx);
            //     ascent.Push(pointer);
            //
            //     idx = label.ContinuationAddress;
            //     
            //     switch ((OpCode)label.Instruction)
            //     {
            //         case OpCode.If: ascent.Push(("InstIf", 0));
            //             break;
            //         case OpCode.Else: ascent.Push(("InstElse", 1));
            //             break;
            //         case OpCode.Block: ascent.Push(("InstBlock", 0));
            //             break;
            //         case OpCode.Loop: ascent.Push(("InstLoop", 0));
            //             break;
            //     }
            //     
            // }
            //
            // ascent.Push(("Function", (int)Frame.Index.Value));

            return ascent.Select(a => a).ToList();
        }

        public void ResetStats()
        {
            Stats.Clear();
            foreach (OpCode opcode in Enum.GetValues(typeof(OpCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (GcCode opcode in Enum.GetValues(typeof(GcCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (ExtCode opcode in Enum.GetValues(typeof(ExtCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (SimdCode opcode in Enum.GetValues(typeof(SimdCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (AtomCode opcode in Enum.GetValues(typeof(AtomCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }
            foreach (WacsCode opcode in Enum.GetValues(typeof(WacsCode)))
            { 
                Stats[(ushort)(ByteCode)opcode] = new ExecStat();
            }

            for (int i = 0, l = Store.FunctionCount(); i < l; i++)
            {

                if (Store[new FuncAddr(i)] is FunctionInstance inst)
                {
                    inst.CallCount = 0;
                }
                if (!Stats.TryGetValue((ushort)i, out var stat))
                {
                    Stats[(ushort)i] = new ExecStat();
                }
            }
        }

        public OpCode GetEndFor() => 
            FindLabel(0) is { } label ? label.Op : OpCode.Func;

        public ModuleInstance? GetModule(FuncAddr funcAddr)
        {
            var functionInstance = Store[funcAddr];
            switch (functionInstance)
            {
                case FunctionInstance wasmFunc: return wasmFunc.Module;
                case HostFunction hostFunc:
                //TODO: maybe implement ref binding for host functions
                default:
                    return null;
            }
        }

        public void LinkFunction(FunctionInstance instance)
        {
            InstructionPointer offset = linkedInstructions.Count;
            instance.LinkedOffset = offset;

            LinkOpStackHeight = 0;
            LinkUnreachable = false;
            MaxLinkOpStackHeight = 0;
            ClearLinkLabels();
            PushLabel(new InstExpressionProxy(new Label
            {
                Instruction = OpCode.Func,
                StackHeight = 0,
                Arity = instance.Type.ResultType.Arity
            }));
            LinkLocalCount = instance.Type.ParameterTypes.Arity + instance.Locals.Length;
            linkedInstructions.Append(instance.Body.Instructions.Flatten().Select((inst,idx)=>inst.Link(this, offset+idx)));
            
            instance.Length = linkedInstructions.Count - instance.LinkedOffset;
            instance.MaxStack = MaxLinkOpStackHeight;
        }
    }
}