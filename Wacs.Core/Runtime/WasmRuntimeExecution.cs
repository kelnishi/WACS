// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Linq;
using System.Reflection;
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
        private IInstruction? lastInstruction = null;
        
        public TDelegate CreateInvoker<TDelegate>(FuncAddr funcAddr, InvokerOptions? options = default)
            where TDelegate : Delegate
        {
            options ??= new InvokerOptions();
            var funcInst = Context.Store[funcAddr];
            var funcType = funcInst.Type;

            if (funcType.ResultType.Types.Length > 1)
                throw new WasmRuntimeException("Binding multiple return values from wasm are not yet supported.");
            
            Delegates.ValidateFunctionTypeCompatibility(funcType, typeof(TDelegate));
            var inner = CreateInvoker(funcAddr, options);
            var genericDelegate = Delegates.AnonymousFunctionFromType(funcType, args =>
            {
                Value[] results = null!;
                try
                {
                    results = funcType.ParameterTypes.Arity == 0
                        ? inner()
                        : (Value[])GenericFuncsInvoke.Invoke(inner, args);
                    if (funcType.ResultType.Types.Length == 1)
                        return results[0];
                    return results;
                }
                catch (TargetInvocationException exc)
                { //Propagate out any exceptions
                    ExceptionDispatchInfo.Throw(exc.InnerException);
                    //This won't happen
                    return results;
                }
            });
            
            return (TDelegate)Delegates.CreateTypedDelegate(genericDelegate, typeof(TDelegate));
        }

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

        private Delegates.GenericFuncs CreateInvoker(FuncAddr funcAddr, InvokerOptions options)
        {
            return GenericDelegate;
            Value[] GenericDelegate(params object[] args)
            {
                var funcInst = Context.Store[funcAddr];
                var funcType = funcInst.Type;
                
                Context.OpStack.PushScalars(funcType.ParameterTypes, args);

                if (options.CollectStats != StatsDetail.None)
                {
                    Context.ResetStats();
                    Context.InstructionTimer.Reset();
                }

                Context.ProcessTimer.Restart();
                
                var task = Context.Invoke(funcAddr);
                task.Wait();
                
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

                return results;
            }
        }
        
        public async Task ProcessThreadAsync(long gasLimit)
        {
            if (gasLimit <= 0) gasLimit = long.MaxValue;
            while (Context.Next() is { } inst)
            {
                int work;
                if (inst.IsAsync)
                    work = await inst.ExecuteAsync(Context);
                else
                    work = inst.Execute(Context);
                
                Context.steps += work;
                if (Context.steps >= gasLimit)
                    throw new InsufficientGasException($"Invocation ran out of gas (limit:{gasLimit}).");
            }
        }

        public async Task ProcessThreadWithOptions(InvokerOptions options)
        {
            long highwatermark = 0;
            long gasLimit = options.GasLimit > 0 ? options.GasLimit : long.MaxValue;
            while (Context.Next() is { } inst)
            {
                //Trace execution
                if (options.LogInstructionExecution != InstructionLogging.None)
                {
                    LogPreInstruction(options, inst);
                }

                int work = 0;
                if (options.CollectStats == StatsDetail.Instruction)
                {
                    Context.InstructionTimer.Restart();
                    
                    if (inst.IsAsync)
                        work = await inst.ExecuteAsync(Context);
                    else
                        work = inst.Execute(Context);

                    Context.InstructionTimer.Stop();
                    Context.steps += work;

                    var st = Context.Stats[(ushort)inst.Op];
                    st.count += work;
                    st.duration += Context.InstructionTimer.ElapsedTicks;
                    Context.Stats[(ushort)inst.Op] = st;
                }
                else
                {
                    Context.InstructionTimer.Start();
                    if (inst.IsAsync)
                        work = await inst.ExecuteAsync(Context);
                    else
                        work = inst.Execute(Context);
                    Context.InstructionTimer.Stop();
                    Context.steps += work;
                }

                if (options.LogInstructionExecution.Has(InstructionLogging.Computes))
                {
                    LogPostInstruction(options, inst);
                }
            
                lastInstruction = inst;
                
                if (Context.steps >= gasLimit)
                    throw new InsufficientGasException($"Invocation ran out of gas (limit:{gasLimit}).");
                
                if (options.LogProgressEvery > 0)
                {
                    highwatermark += work;
                    if (highwatermark >= options.LogProgressEvery)
                    {
                        highwatermark -= options.LogProgressEvery;
                        Console.Error.Write('.');
                    }
                }
            }
        }

        private void LogPreInstruction(InvokerOptions options, IInstruction inst)
        {
            switch ((OpCode)inst.Op)
            {
                //Handle these post
                case var _ when IInstruction.IsNumeric(inst): break;
                case var _ when IInstruction.IsVar(inst): break;
                case var _ when IInstruction.IsLoad(inst): break;
                
                case OpCode.Call when options.LogInstructionExecution.Has(InstructionLogging.Binds) && IInstruction.IsBound(Context, inst):
                case OpCode.CallIndirect when options.LogInstructionExecution.Has(InstructionLogging.Binds) && IInstruction.IsBound(Context, inst):
                // case OpCode.CallRef when options.LogInstructionExecution.Has(InstructionLogging.Binds) && IInstruction.IsBound(Context, inst):
                
                case OpCode.Call when options.LogInstructionExecution.Has(InstructionLogging.Calls):
                case OpCode.CallIndirect when options.LogInstructionExecution.Has(InstructionLogging.Calls):
                // case OpCode.CallRef when options.LogInstructionExecution.Has(InstructionLogging.Calls):
                case OpCode.Return when options.LogInstructionExecution.Has(InstructionLogging.Calls):
                case OpCode.ReturnCallIndirect when options.LogInstructionExecution.Has(InstructionLogging.Calls):
                case OpCode.ReturnCall when options.LogInstructionExecution.Has(InstructionLogging.Calls):
                case OpCode.End when options.LogInstructionExecution.Has(InstructionLogging.Calls) && Context.GetEndFor() == OpCode.Func:
                        
                case OpCode.Block when options.LogInstructionExecution.Has(InstructionLogging.Blocks):
                case OpCode.Loop when options.LogInstructionExecution.Has(InstructionLogging.Blocks):
                case OpCode.If when options.LogInstructionExecution.Has(InstructionLogging.Blocks):
                case OpCode.Else when options.LogInstructionExecution.Has(InstructionLogging.Blocks):
                case OpCode.End when options.LogInstructionExecution.Has(InstructionLogging.Blocks) && Context.GetEndFor() == OpCode.Block:
                            
                case OpCode.Br when options.LogInstructionExecution.Has(InstructionLogging.Branches):
                case OpCode.BrIf when options.LogInstructionExecution.Has(InstructionLogging.Branches):
                case OpCode.BrTable when options.LogInstructionExecution.Has(InstructionLogging.Branches):
                
                case var _ when IInstruction.IsBranch(lastInstruction) && options.LogInstructionExecution.Has(InstructionLogging.Branches):
                case var _ when options.LogInstructionExecution.Has(InstructionLogging.Computes):
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
                        var log = $"Instruction: {inst.RenderText(Context)}".PadRight(40, ' ') + location;
                        Console.Error.WriteLine(log);
                    }
                    break; 
            }
        }

        private void LogPostInstruction(InvokerOptions options, IInstruction inst)
        {
            if ((options.LogInstructionExecution & InstructionLogging.Computes) == 0)
                return;
            
            switch ((OpCode)inst.Op)
            {
                case var _ when IInstruction.IsLoad(inst):
                case var _ when IInstruction.IsNumeric(inst): 
                case var _ when IInstruction.IsVar(inst):
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
                        var log = $"Instruction: {inst.RenderText(Context)}".PadRight(40, ' ') + location;
                        Console.Error.WriteLine(log);
                    }
                    break; 
                default: return;
            }
        }

        private void PrintStats(InvokerOptions options)
        {
            long procTicks = Context.ProcessTimer.ElapsedTicks; //ns
            long totalExecs = options.CollectStats == StatsDetail.Instruction
                ? Context.Stats.Values.Sum(dc => dc.count)
                : Context.steps;
            long execTicks = options.CollectStats == StatsDetail.Instruction
                ? Context.Stats.Values.Sum(dc => dc.duration)
                : Context.InstructionTimer.ElapsedTicks;
            long overheadTicks = procTicks - execTicks;

            TimeSpan totalTime = new TimeSpan(procTicks/100); //100ns
            TimeSpan execTime = new TimeSpan(execTicks/100);
            TimeSpan overheadTime = new TimeSpan(overheadTicks/100);
            double overheadPercent =  100.0 * overheadTicks / procTicks;
            double execPercent = 100.0 * execTicks / procTicks;
            string overheadLabel = $"({overheadPercent:#0.###}%) {overheadTime:g}";
            
            string totalLabel = "    total duration";
            string totalInst = $"{totalExecs}";
            string totalPercent = $"{execPercent:#0.###}%t".PadLeft(8,' ');
            string avgTime = $"{execTime.TotalMilliseconds * 1000000.0/totalExecs:#0.###}ns/i";
            double instPerSec = totalExecs * 1000.0 / totalTime.TotalMilliseconds;
            string velocity = $"{instPerSec.SiSuffix("0.###")}i/s";
            Console.Error.WriteLine($"Execution Stats:");
            Console.Error.WriteLine($"{totalLabel}: {totalInst}| ({totalPercent}) {execTime.TotalSeconds:#0.###}s {avgTime} {velocity} overhead:{overheadLabel} total proctime:{totalTime.TotalSeconds:#0.###}s");
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