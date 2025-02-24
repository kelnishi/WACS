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
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Utilities;
using Wacs.Core.WASIp1;

namespace Wacs.Core.Runtime
{
    public partial class WasmRuntime
    {
        private InstructionBase? lastInstruction = null;
        public bool TraceExecution = false;

        private Delegate CreateInvokerInternal(FuncAddr funcAddr, Type delegateType, bool returnsResult, InvokerOptions? options = default)
        {
            options ??= new InvokerOptions();
            var funcInst = Context.Store[funcAddr];
            var funcType = funcInst.Type;
            
            Delegates.ValidateFunctionTypeCompatibility(funcType, delegateType);
            
            int arity = funcType.ResultType.Types.Length;
            
            if (returnsResult)
            {
                if (arity != 1)
                    throw new WasmRuntimeException($"Delegate type requires 1 return value, found {arity}");
            }
            else
            {
                if (arity != 0)
                    throw new WasmRuntimeException($"Delegate type has no return value, found {arity}");
            }
            
            var invoker = CreateInvoker(funcAddr, options);
            return Delegates.AnonymousFunctionFromType(funcInst.Type, invoker);
        }

        /// <summary>
        /// Bind a wasm Func to C# via dynamic invocation (AOT compatible)
        /// </summary>
        public Func<TResult> CreateInvokerFunc<TResult>(FuncAddr funcAddr, InvokerOptions? options = default) => 
            () => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<TResult>), true, options).DynamicInvoke()!;

        public Func<T1,TResult> CreateInvokerFunc<T1,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) => 
            (arg1) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,TResult>), true, options).DynamicInvoke(arg1)!;

        public Func<T1,T2,TResult> CreateInvokerFunc<T1,T2,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) => 
            (arg1, arg2) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,TResult>), true, options).DynamicInvoke(arg1, arg2)!;

        public Func<T1,T2,T3,TResult> CreateInvokerFunc<T1,T2,T3,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) => 
            (arg1, arg2, arg3) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3)!;

        public Func<T1,T2,T3,T4,TResult> CreateInvokerFunc<T1,T2,T3,T4,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) => 
            (arg1, arg2, arg3, arg4) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,T4,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3, arg4)!;

        public Func<T1,T2,T3,T4,T5,TResult> CreateInvokerFunc<T1,T2,T3,T4,T5,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,T4,T5,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5)!;

        public Func<T1,T2,T3,T4,T5,T6,TResult> CreateInvokerFunc<T1,T2,T3,T4,T5,T6,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,T4,T5,T6,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6)!;

        public Func<T1,T2,T3,T4,T5,T6,T7,TResult> CreateInvokerFunc<T1,T2,T3,T4,T5,T6,T7,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6, arg7) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,T4,T5,T6,T7,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7)!;

        public Func<T1,T2,T3,T4,T5,T6,T7,T8,TResult> CreateInvokerFunc<T1,T2,T3,T4,T5,T6,T7,T8,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,T4,T5,T6,T7,T8,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8)!;

        public Func<T1,T2,T3,T4,T5,T6,T7,T8,T9,TResult> CreateInvokerFunc<T1,T2,T3,T4,T5,T6,T7,T8,T9,TResult>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9) => (TResult)CreateInvokerInternal(funcAddr, typeof(Func<T1,T2,T3,T4,T5,T6,T7,T8,T9,TResult>), true, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9)!;


        public Action CreateInvokerAction(FuncAddr funcAddr, InvokerOptions? options = default) =>
            () =>
            {
                var funcInst = Context.Store[funcAddr];
                var invoker = CreateInvokerInternal(funcAddr, typeof(Action), false, options);
                invoker.DynamicInvoke();
            };

        public Action<T1> CreateInvokerAction<T1>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1) => { CreateInvokerInternal(funcAddr, typeof(Action<T1>), false, options).DynamicInvoke(arg1); };

        public Action<T1,T2> CreateInvokerAction<T1,T2>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2>), false, options).DynamicInvoke(arg1, arg2); };

        public Action<T1,T2,T3> CreateInvokerAction<T1,T2,T3>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3>), false, options).DynamicInvoke(arg1, arg2, arg3); };

        public Action<T1,T2,T3,T4> CreateInvokerAction<T1,T2,T3,T4>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3,T4>), false, options).DynamicInvoke(arg1, arg2, arg3, arg4); };

        public Action<T1,T2,T3,T4,T5> CreateInvokerAction<T1,T2,T3,T4,T5>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3,T4,T5>), false, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5); };

        public Action<T1,T2,T3,T4,T5,T6> CreateInvokerAction<T1,T2,T3,T4,T5,T6>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3,T4,T5,T6>), false, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6); };

        public Action<T1,T2,T3,T4,T5,T6,T7> CreateInvokerAction<T1,T2,T3,T4,T5,T6,T7>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6, arg7) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3,T4,T5,T6,T7>), false, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7); };

        public Action<T1,T2,T3,T4,T5,T6,T7,T8> CreateInvokerAction<T1,T2,T3,T4,T5,T6,T7,T8>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3,T4,T5,T6,T7,T8>), false, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8); };

        public Action<T1,T2,T3,T4,T5,T6,T7,T8,T9> CreateInvokerAction<T1,T2,T3,T4,T5,T6,T7,T8,T9>(FuncAddr funcAddr, InvokerOptions? options = default) =>
            (arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9) => { CreateInvokerInternal(funcAddr, typeof(Action<T1,T2,T3,T4,T5,T6,T7,T8,T9>), false, options).DynamicInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9); };

        //No type checking, but you can get multiple return values
        public Delegates.StackFunc CreateStackInvoker(FuncAddr funcAddr, InvokerOptions? options = default)
        {
            options ??= new InvokerOptions();
            var invoker = CreateInvoker(funcAddr, options);
            var funcInst = Context.Store[funcAddr];
            var funcType = funcInst.Type;
            object[] p = new object[funcType.ParameterTypes.Arity];
            
            return valueParams =>
            {
                for (int i = 0; i < funcType.ParameterTypes.Arity; ++i)
                    p[i] = valueParams[i];

                return invoker(p);
            };
        }

        public Delegates.GenericFuncsAsync CreateStackInvokerAsync(FuncAddr funcAddr, InvokerOptions? options = default)
        {
            options ??= new InvokerOptions();
            return CreateInvokerAsync(funcAddr, options);
        }

        private Delegates.GenericFuncsAsync CreateInvokerAsync(FuncAddr funcAddr, InvokerOptions options)
        {
            if (options.SynchronousExecution)
                throw new NotSupportedException("Synchronous execution is not supported for async invokers.");
            
            return GenericDelegateAsync;
            async Task<Value[]> GenericDelegateAsync(params Value[] args)
            {
                var funcInst = Context.Store[funcAddr];
                var funcType = funcInst.Type;
                
                Context.OpStack.PushValues(args);

                if (options.CollectStats != StatsDetail.None)
                {
                    Context.ResetStats();
                    Context.InstructionTimer.Reset();
                }

                Context.ProcessTimer.Restart();
                Context.InstructionTimer.Restart();
                Context.InstructionPointer = ExecContext.AbortSequence;
                
                await Context.InvokeAsync(funcAddr);
                
                Context.steps = 0;
                bool fastPath = options.UseFastPath();
                try
                {
                    if (fastPath)
                    {
                        await ProcessThreadAsync(options.GasLimit);
                    }
                    else
                    {
                        await ProcessThreadWithOptions(options);
                    }
                }
                catch (AggregateException agg)
                {
                    var exc = agg.InnerException;
                    Context.ProcessTimer.Stop();
                    Context.InstructionTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats != StatsDetail.None)
                        PrintStats(options);
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);

                        ExceptionDispatchInfo.Throw(new TrapException(exc.Message + $":line {line} instruction #{Context.steps}\n{path}"));
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();
                    ExceptionDispatchInfo.Throw(exc);
                }
                catch (TrapException exc)
                {
                    Context.ProcessTimer.Stop();
                    Context.InstructionTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats != StatsDetail.None)
                        PrintStats(options);
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);

                        ExceptionDispatchInfo.Throw(new TrapException(exc.Message + $":line {line} instruction #{Context.steps}\n{path}"));
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();
                    ExceptionDispatchInfo.Throw(exc);
                }
                catch (SignalException exc)
                {
                    Context.ProcessTimer.Stop();
                    Context.InstructionTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats != StatsDetail.None)
                        PrintStats(options);
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                    string message = exc.Message;
                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);
                        message = exc.Message + $":line {line} instruction #{Context.steps}\n{path}";
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();

                    var exType = exc.GetType();
                    var ctr = exType.GetConstructor(new Type[] { typeof(int), typeof(string) });
                    ExceptionDispatchInfo.Throw(ctr?.Invoke(new object[] { exc.Signal, message }) as Exception ?? exc);
                }
                catch (WasmRuntimeException)
                {
                    //Maybe Log?
                    Context.FlushCallStack();
                    throw;
                }
                
                Context.ProcessTimer.Stop();
                Context.InstructionTimer.Stop();
                if (options.LogProgressEvery > 0) Console.Error.WriteLine("done.");
                if (options.CollectStats != StatsDetail.None) PrintStats(options);
                if (options.LogGas) Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                Value[] results = new Value[funcType.ResultType.Arity];
                Context.OpStack.PopScalars(funcType.ResultType, results);

                return results;
            }
        }

        public Delegates.GenericFuncs CreateInvoker(FuncAddr funcAddr, InvokerOptions options)
        {
            return GenericDelegate;
            Value[] GenericDelegate(params object[] args)
            {
                var funcInst = Context.Store[funcAddr];
                var funcType = funcInst.Type;

                if (Context.OpStack.Count > 0)
                    throw new WasmRuntimeException("Values left on operand stack");
                
                Context.OpStack.PushScalars(funcType.ParameterTypes, args);

                if (options.CollectStats != StatsDetail.None)
                {
                    Context.ResetStats();
                    Context.InstructionTimer.Reset();
                }

                Context.ProcessTimer.Restart();
                Context.InstructionTimer.Restart();
                Context.InstructionPointer = ExecContext.AbortSequence;

                if (options.SynchronousExecution)
                {
                    Context.Invoke(funcAddr);
                }
                else
                {
                    var task = Context.InvokeAsync(funcAddr);
                    task.Wait();
                }
                Context.steps = 0;
                bool fastPath = options.UseFastPath();
                try
                {
                    if (fastPath)
                    {
                        Task thread = ProcessThreadAsync(options.GasLimit);
                        thread.Wait();
                    }
                    else
                    {
                        Task thread = ProcessThreadWithOptions(options);
                        thread.Wait();
                    }
                }
                catch (AggregateException agg)
                {
                    var exc = agg.InnerException;
                    Context.ProcessTimer.Stop();
                    Context.InstructionTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats != StatsDetail.None)
                        PrintStats(options);
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);

                        ExceptionDispatchInfo.Throw(new TrapException(exc.Message + $":line {line} instruction #{Context.steps}\n{path}"));
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();
                    ExceptionDispatchInfo.Throw(exc);
                }
                catch (TrapException exc)
                {
                    Context.ProcessTimer.Stop();
                    Context.InstructionTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats != StatsDetail.None)
                        PrintStats(options);
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);

                        ExceptionDispatchInfo.Throw(new TrapException(exc.Message + $":line {line} instruction #{Context.steps}\n{path}"));
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();
                    ExceptionDispatchInfo.Throw(exc);
                }
                catch (SignalException exc)
                {
                    Context.ProcessTimer.Stop();
                    Context.InstructionTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats != StatsDetail.None)
                        PrintStats(options);
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                    string message = exc.Message;
                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);
                        message = exc.Message + $":line {line} instruction #{Context.steps}\n{path}";
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();

                    var exType = exc.GetType();
                    var ctr = exType.GetConstructor(new Type[] { typeof(int), typeof(string) });
                    ExceptionDispatchInfo.Throw(ctr?.Invoke(new object[] { exc.Signal, message }) as Exception ?? exc);
                }
                catch (WasmRuntimeException)
                {
                    //Maybe Log?
                    Context.FlushCallStack();
                    throw;
                }
                
                Context.ProcessTimer.Stop();
                Context.InstructionTimer.Stop();
                if (options.LogProgressEvery > 0) Console.Error.WriteLine("done.");
                if (options.CollectStats != StatsDetail.None) PrintStats(options);
                if (options.LogGas) Console.Error.WriteLine($"Process used {Context.steps} gas. {Context.ProcessTimer.Elapsed}");

                Value[] results = new Value[funcType.ResultType.Arity];
                var span = results.AsSpan();
                Context.OpStack.PopScalars(funcType.ResultType, span);

                Context.GetModule(funcAddr)?.DerefTypes(span);
                
                // if (Context.OpStack.Count > 0)
                //     throw new WasmRuntimeException("Values left on operand stack");
                while (Context.OpStack.HasValue)
                    Context.OpStack.PopAny();
                
                return results;
            }
        }

        public async Task ProcessThreadAsync(long gasLimit)
        {
            InstructionBase inst;
            if (gasLimit <= 0)
            {
                while (++Context.InstructionPointer >= 0)
                {
                    inst = Context._currentSequence[Context.InstructionPointer];
                    if (inst.PointerAdvance > 0)
                        Context.InstructionPointer += inst.PointerAdvance;
                    if (inst.Nop)
                        continue;
                    
                    if (inst.IsAsync)
                    {
                        await inst.ExecuteAsync(Context);
                    }
                    else
                    {
                        inst.Execute(Context);
                    }
                }
            }
            else
            {
                while (++Context.InstructionPointer >= 0)
                {
                    inst = Context._currentSequence[Context.InstructionPointer];
                    //Counting gas costs about 18% throughput!
                    Context.steps += inst.Size;
                    if (inst.PointerAdvance > 0)
                        Context.InstructionPointer += inst.PointerAdvance;
                    if (inst.Nop)
                        continue;
                    
                    if (inst.IsAsync)
                    {
                        await inst.ExecuteAsync(Context);
                    }
                    else
                    {
                        inst.Execute(Context);
                    }
                    
                    if (Context.steps >= gasLimit)
                    {
                        throw new InsufficientGasException($"Invocation ran out of gas (limit:{gasLimit}).");
                    }
                }
            }
        }

        public async Task ProcessThreadWithOptions(InvokerOptions options) 
        {
            long highwatermark = 0;
            long gasLimit = options.GasLimit > 0 ? options.GasLimit : long.MaxValue;
            InstructionBase inst;
            
            while (++Context.InstructionPointer >= 0)
            {
                inst = Context._currentSequence[Context.InstructionPointer];
            
                //Trace execution
                if (options.LogInstructionExecution != InstructionLogging.None)
                {
                    LogPreInstruction(options, inst);
                }

                if (options.CollectStats == StatsDetail.Instruction)
                {
                    Context.InstructionTimer.Restart();
                    if (inst.PointerAdvance > 0)
                        Context.InstructionPointer += inst.PointerAdvance;
                    if (inst.Nop)
                        continue;
                    
                    if (inst.IsAsync)
                        await inst.ExecuteAsync(Context);
                    else
                        inst.Execute(Context);

                    Context.InstructionTimer.Stop();
                    Context.steps += inst.Size;

                    var st = Context.Stats[(ushort)inst.Op];
                    st.count += inst.Size;
                    st.duration += Context.InstructionTimer.ElapsedTicks;
                    Context.Stats[(ushort)inst.Op] = st;
                }
                else
                {
                    Context.InstructionTimer.Start();
                    if (inst.PointerAdvance > 0)
                        Context.InstructionPointer += inst.PointerAdvance;
                    if (inst.Nop)
                        continue;
                    
                    if (inst.IsAsync)
                        await inst.ExecuteAsync(Context);
                    else
                        inst.Execute(Context);
                    Context.InstructionTimer.Stop();
                    Context.steps += inst.Size;
                }

                if (((int)options.LogInstructionExecution & (int)InstructionLogging.Computes) != 0)
                {
                    LogPostInstruction(options, inst);
                }
            
                lastInstruction = inst;
                
                if (Context.steps >= gasLimit)
                    throw new InsufficientGasException($"Invocation ran out of gas (limit:{gasLimit}).");
                
                if (options.LogProgressEvery > 0)
                {
                    highwatermark += inst.Size;
                    if (highwatermark >= options.LogProgressEvery)
                    {
                        highwatermark -= options.LogProgressEvery;
                        Console.Error.Write('.');
                    }
                }
            }
        }

        private void LogPreInstruction(InvokerOptions options, InstructionBase inst)
        {
            switch ((OpCode)inst.Op)
            {
                //Handle these post
                case var _ when InstructionBase.IsNumeric(inst): break;
                case var _ when InstructionBase.IsVar(inst): break;
                case var _ when InstructionBase.IsLoad(inst): break;
                
                case OpCode.Call when ((int)options.LogInstructionExecution&(int)InstructionLogging.Binds)!=0 && InstructionBase.IsBound(Context, inst):
                case OpCode.CallIndirect when ((int)options.LogInstructionExecution&(int)InstructionLogging.Binds)!=0 && InstructionBase.IsBound(Context, inst):
                // case OpCode.CallRef when options.LogInstructionExecution&(int)InstructionLogging.Binds) && InstructionBase.IsBound(Context, inst):
                
                case OpCode.Call when ((int)options.LogInstructionExecution&(int)InstructionLogging.Calls)!=0:
                case OpCode.CallIndirect when ((int)options.LogInstructionExecution&(int)InstructionLogging.Calls)!=0:
                // case OpCode.CallRef when options.LogInstructionExecution&(int)InstructionLogging.Calls):
                case OpCode.Return when ((int)options.LogInstructionExecution&(int)InstructionLogging.Calls)!=0:
                case OpCode.ReturnCallIndirect when ((int)options.LogInstructionExecution&(int)InstructionLogging.Calls)!=0:
                case OpCode.ReturnCall when ((int)options.LogInstructionExecution&(int)InstructionLogging.Calls)!=0:
                case OpCode.End when ((int)options.LogInstructionExecution&(int)InstructionLogging.Calls)!=0 && Context.GetEndFor() == OpCode.Func:
                        
                case OpCode.Block when ((int)options.LogInstructionExecution&(int)InstructionLogging.Blocks)!=0:
                case OpCode.Loop when ((int)options.LogInstructionExecution&(int)InstructionLogging.Blocks)!=0:
                case OpCode.If when ((int)options.LogInstructionExecution&(int)InstructionLogging.Blocks)!=0:
                case OpCode.Else when ((int)options.LogInstructionExecution&(int)InstructionLogging.Blocks)!=0:
                case OpCode.End when ((int)options.LogInstructionExecution&(int)InstructionLogging.Blocks)!=0 && Context.GetEndFor() == OpCode.Block:
                            
                case OpCode.Br when ((int)options.LogInstructionExecution&(int)InstructionLogging.Branches)!=0:
                case OpCode.BrIf when ((int)options.LogInstructionExecution&(int)InstructionLogging.Branches)!=0:
                case OpCode.BrTable when ((int)options.LogInstructionExecution&(int)InstructionLogging.Branches)!=0:
                
                case var _ when InstructionBase.IsBranch(lastInstruction) && ((int)options.LogInstructionExecution&(int)InstructionLogging.Branches)!=0:
                case var _ when ((int)options.LogInstructionExecution&(int)InstructionLogging.Computes)!=0:
                    string location = "";
                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);
                        location = $"line {line.ToString().PadLeft(7,' ')}";
                        if (options.ShowPath)
                            location += $":{path}";
                            
                        var log = $"{location}: {inst.RenderText(Context)}".PadRight(40, ' ');
                        Console.Error.WriteLine(log);
                    }
                    else
                    {
                        var log = $"Inst[0x{Context.InstructionPointer:x8}]: {inst.RenderText(Context)}".PadRight(40, ' ') + location;
                        Console.Error.WriteLine(log);
                    }
                    break; 
            }
        }

        private void LogPostInstruction(InvokerOptions options, InstructionBase inst)
        {
            if ((options.LogInstructionExecution & InstructionLogging.Computes) == 0)
                return;
            
            switch ((OpCode)inst.Op)
            {
                case var _ when InstructionBase.IsLoad(inst):
                case var _ when InstructionBase.IsNumeric(inst): 
                case var _ when InstructionBase.IsVar(inst):
                    string location = "";
                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);
                        location = $"line {line.ToString().PadLeft(7,' ')}";
                        if (options.ShowPath)
                            location += $":{path}";
                            
                        var log = $"{location}: {inst.RenderText(Context)}".PadRight(40, ' ');
                        Console.Error.WriteLine(log);
                    }
                    else
                    {
                        var log = $"Inst[0x{Context.InstructionPointer:x8}]: {inst.RenderText(Context)}".PadRight(40, ' ') + location;
                        Console.Error.WriteLine(log);
                    }
                    break; 
                default: return;
            }
        }

        private void PrintStats(InvokerOptions options)
        {
            long procTicks = Context.ProcessTimer.ElapsedTicks;
            long totalExecs = options.CollectStats == StatsDetail.Instruction
                ? Context.Stats.Values.Sum(dc => dc.count)
                : Context.steps;
            long execTicks = options.CollectStats == StatsDetail.Instruction
                ? Context.Stats.Values.Sum(dc => dc.duration)
                : Context.InstructionTimer.ElapsedTicks;
            long overheadTicks = procTicks - execTicks;

            long scale = Stopwatch.Frequency / 1000_000_0; //100ns ticks

            TimeSpan totalTime = new TimeSpan(procTicks/scale);
            TimeSpan execTime = new TimeSpan(execTicks/scale);
            TimeSpan overheadTime = new TimeSpan(overheadTicks/scale);
            double overheadPercent =  100.0 * overheadTicks / procTicks;
            double execPercent = 100.0 * execTicks / procTicks;
            string overheadLabel = $" overhead:({overheadPercent:#0.###}%) {overheadTime.TotalSeconds:#0.###}s";
            
            string totalLabel = "    total duration";
            string totalInst = $"{totalExecs}";
            string totalPercent = $" ({execPercent:#0.###}%t)".PadLeft(8,' ');
            string avgTime = $"{execTime.TotalMilliseconds * 1000000.0/totalExecs:#0.###}ns/i";
            double instPerSec = totalExecs * 1000.0 / totalTime.TotalMilliseconds;
            string velocity = $"{instPerSec.SiSuffix("0.###")}ips";

            if (options.CollectStats == StatsDetail.Total)
            {
                overheadLabel = "";
                totalPercent = "";
            }
            
            Console.Error.WriteLine($"Execution Stats:");
            Console.Error.WriteLine($"{totalLabel}: {totalInst}|{totalPercent} {execTime.TotalSeconds:#0.###}s {avgTime} {velocity}{overheadLabel} proctime:{totalTime.TotalSeconds:#0.###}s");
            var orderedStats = Context.Stats
                .Where(bdc => bdc.Value.count != 0)
                .OrderBy(bdc => -bdc.Value.count);
            
            foreach (var (opcode, st) in orderedStats)
            {
                string label = $"{((ByteCode)opcode).GetMnemonic()}".PadLeft(totalLabel.Length, ' ');
                TimeSpan instTime = new TimeSpan(st.duration/100); //100ns
                double percent = 100.0 * st.duration / execTicks;
                string execsLabel = $"{st.count}".PadLeft(totalInst.Length, ' ');
                string percentLabel = $"{percent:#0.###}%e".PadLeft(8,' ');
                string instAve = $"{instTime.TotalMilliseconds * 1000000.0/st.count:#0.#}ns/i";
                Console.Error.WriteLine($"{label}: {execsLabel}| ({percentLabel}) {instTime.TotalMilliseconds:#0.000}ms {instAve}");
            }
        }
    }
}