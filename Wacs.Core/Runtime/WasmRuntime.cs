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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.WASIp1;

// using System.Diagnostics.CodeAnalysis;

namespace Wacs.Core.Runtime
{
    public partial class WasmRuntime
    {
        private static readonly MethodInfo GenericFuncsInvoke = typeof(Delegates.GenericFuncs).GetMethod("Invoke")!;
        private readonly Dictionary<(string module, string entity), IAddress?> _entityBindings = new();

        private readonly List<ModuleInstance> _moduleInstances = new();
        private readonly Dictionary<string, ModuleInstance> _registeredModules = new();

        private readonly ExecContext Context;

        private readonly Store Store;

        private IInstruction? lastInstruction = null;

        public WasmRuntime(RuntimeAttributes? attributes = null)
        {
            Store = new Store();
            Context = new ExecContext(Store, attributes);
        }

        public IInstructionFactory InstructionFactory => Context.InstructionFactory;

        public void RegisterModule(string moduleName, ModuleInstance moduleInstance)
        {
            _registeredModules[moduleName] = moduleInstance;

            //Bind exports
            foreach (var export in moduleInstance.Exports)
            {
                _entityBindings[(moduleName, export.Name)] = export.Value switch
                {
                    ExternalValue.Function func => func.Address,
                    ExternalValue.Table table => table.Address,
                    ExternalValue.Memory mem => mem.Address,
                    ExternalValue.Global global => global.Address,
                    _ => throw new InvalidDataException($"Corrupted Export Instance in {moduleName} ({export.Name})"),
                };
            }
        }

        public ModuleInstance GetModule(string? moduleName)
        {
            if (moduleName == null)
                return _moduleInstances.Last();
            
            if (_registeredModules.TryGetValue(moduleName, out var moduleInstance))
            {
                return moduleInstance;
            }
            
            var anonInstance = _moduleInstances.FirstOrDefault(m => m.Name == moduleName);
            if (anonInstance != null)
                return anonInstance;
            
            throw new Exception($"Module '{moduleName}' not found.");
        }

        public bool TryGetExportedFunction(string entity, out FuncAddr addr)
        {
            var exports = _moduleInstances.SelectMany(modInst => modInst.Exports)
                .Where(export => export.Name == entity)
                .Select(export => export.Value)
                .Cast<ExternalValue.Function>()
                .Select(func => func.Address);
            addr = exports.LastOrDefault();
            return addr != null;
        }

        public bool TryGetExportedFunction((string module, string entity) id, out FuncAddr addr)
        {
            try
            {
                addr = GetExportedFunction(id);
                return true;
            }
            catch (UnboundEntityException)
            {
                var exports = _moduleInstances
                    .Where(modInst => modInst.Name == id.module)
                    .SelectMany(modInst => modInst.Exports)
                    .Where(export => export.Name == id.entity)
                    .Select(export => export.Value)
                    .Cast<ExternalValue.Function>()
                    .Select(func => func.Address)
                    .ToList();

                if (exports.Count > 0)
                {
                    addr = exports.Last();
                    return true;
                }
                addr = FuncAddr.Null;
                return false;
            }
        }

        public FuncAddr GetExportedFunction((string module, string entity) id)
        {
            if (GetBoundEntity(id) is FuncAddr addr)
            {
                return addr;
            }

            throw new UnboundEntityException($"Function {id} was not exported from any modules currently loaded in the runtime.");
        }

        private IAddress? GetBoundEntity((string module, string entity) id) =>
            _entityBindings.GetValueOrDefault(id);

        // [RequiresUnreferencedCode("Uses reflection to match parameters for binding")]
        public void BindHostFunction<TDelegate>((string module, string entity) id, TDelegate func)
            where TDelegate : Delegate
        {
            var funcType = func.GetType();
            var parameters = funcType.GetMethod("Invoke")?.GetParameters();
            var paramTypes = parameters?
                                 .Where(p=> !p.Attributes.HasFlag(ParameterAttributes.Out))
                                 .Select(p => p.ParameterType)
                                 .ToArray()
                             ?? Array.Empty<Type>();
            var outTypes = parameters?
                               .Where(p=> p.Attributes.HasFlag(ParameterAttributes.Out))
                               .Select(p => p.ParameterType)
                               .ToArray()
                           ?? Array.Empty<Type>();
            
            var returnTypeInfo = funcType.GetMethod("Invoke")?.ReturnType;
            
            var paramValTypes = new ResultType(paramTypes);
            var outValTypes = outTypes.Select(t => ValTypeUtilities.UnpackRef(t)).ToArray();
            var returnType = returnTypeInfo?.ToValType() ?? ValType.Nil;

            if (returnType != ValType.Nil)
            {
                outValTypes = new ValType[] { returnType }.Concat(outValTypes).ToArray();
            }
            var returnValType = new ResultType(outValTypes);
            
            for (int i = paramValTypes.Types.Length - 1; i >= 0; --i)
            {
                if (paramValTypes.Types[i] == ValType.ExecContext)
                {
                    if (i > 0)
                    {
                        throw new ArgumentException(
                            "ExecContext may only be the first parameter of a bound host function.");
                    }
                    //If it's the first, just unshift it.
                    paramValTypes = new ResultType(paramValTypes.Types.Skip(1).ToArray());
                }
            }

            Store.OpenTransaction();
            var type = new FunctionType(paramValTypes, returnValType);
            var funcAddr = AllocateHostFunc(Store, id, type, funcType, func);
            Store.CommitTransaction();
            _entityBindings[id] = funcAddr;
        }

        public string GetFunctionName(FuncAddr funcAddr)
        {
            if (!Context.Store.Contains(funcAddr))
                throw new ArgumentException($"Runtime did not contain function address.");
            var funcInst = Context.Store[funcAddr];
            return funcInst.Id;
        }

        public FunctionType GetFunctionType(FuncAddr funcAddr)
        {
            if (!Context.Store.Contains(funcAddr))
                throw new ArgumentException($"Runtime did not contain function address.");
            var funcInst = Context.Store[funcAddr];
            return funcInst.Type;
        }

        private Delegates.GenericFuncs CreateInvoker(FuncAddr funcAddr, InvokerOptions options)
        {
            return GenericDelegate;
            Value[] GenericDelegate(params object[] args)
            {
                var funcInst = Context.Store[funcAddr];
                var funcType = funcInst.Type;
                
                Context.OpStack.PushScalars(funcType.ParameterTypes, args);

                if (options.CollectStats)
                {
                    Context.ResetStats();
                    Context.InstructionTimer.Reset();
                }

                Context.ProcessTimer.Restart();

                Context.Invoke(funcAddr);

                long steps = 0;
                bool fastPath = options.UseFastPath();

                try
                {
                    if (fastPath)
                    {
                        int comp = 0;
                        do
                        {
                            comp = ProcessThread();
                            steps += comp;
                            if (options.GasLimit > 0)
                            {
                                if (steps >= options.GasLimit)
                                {
                                    throw new InsufficientGasException(
                                        $"Invocation ran out of gas (limit:{options.GasLimit}).");
                                }
                            }
                        } while (comp > 0);
                    }
                    else
                    {
                        int comp = 0;
                        do
                        {
                            comp = ProcessThreadWithOptions(options);
                            steps += comp;

                            if (options.GasLimit > 0)
                            {
                                if (steps >= options.GasLimit)
                                {
                                    throw new InsufficientGasException(
                                        $"Invocation ran out of gas (limit:{options.GasLimit}).");
                                }
                            }

                            if (options.LogGas && options.LogProgressEvery > 0)
                            {
                                if (steps % options.LogProgressEvery == 0)
                                {
                                    Console.Error.Write('.');
                                }
                            }
                        } while (comp > 0);
                    }
                }
                catch (TrapException exc)
                {
                    Context.ProcessTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats)
                        PrintStats();
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {steps} gas. {Context.ProcessTimer.Elapsed}");

                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);

                        throw new TrapException(exc.Message + $":line {line} instruction #{steps}\n{path}");
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();
                    throw;
                }
                catch (SignalException exc)
                {
                    Context.ProcessTimer.Stop();
                    if (options.LogProgressEvery > 0)
                        Console.Error.WriteLine();
                    if (options.CollectStats)
                        PrintStats();
                    if (options.LogGas)
                        Console.Error.WriteLine($"Process used {steps} gas. {Context.ProcessTimer.Elapsed}");

                    string message = exc.Message;
                    if (options.CalculateLineNumbers)
                    {
                        var ptr = Context.ComputePointerPath();
                        var path = string.Join(".", ptr.Select(t => $"{t.Item1.Capitalize()}[{t.Item2}]"));
                        (int line, string instruction) = Context.Frame.Module.Repr.CalculateLine(path);
                        message = exc.Message + $":line {line} instruction #{steps}\n{path}";
                    }

                    //Flush the stack before throwing...
                    Context.FlushCallStack();

                    var exType = exc.GetType();
                    var ctr = exType.GetConstructor(new Type[] { typeof(int), typeof(string) });
                    throw ctr?.Invoke(new object[] { exc.Signal, message }) as Exception ?? exc;
                }
                catch (WasmRuntimeException)
                {
                    //Maybe Log?
                    Context.FlushCallStack();
                    throw;
                }
                
                Context.ProcessTimer.Stop();
                if (options.LogProgressEvery > 0) Console.Error.WriteLine("done.");
                if (options.CollectStats) PrintStats();
                if (options.LogGas) Console.Error.WriteLine($"Process used {steps} gas. {Context.ProcessTimer.Elapsed}");

                Value[] results = new Value[funcType.ResultType.Arity];
                var span = results.AsSpan();
                Context.OpStack.PopScalars(funcType.ResultType, span);

                return results;
            }
        }

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
                try
                {
                    Value[] results = funcType.ParameterTypes.Arity == 0
                        ? inner()
                        : (Value[])GenericFuncsInvoke.Invoke(inner, args);
                    if (funcType.ResultType.Types.Length == 1)
                        return results[0];
                    return results;
                }
                catch (TargetInvocationException exc)
                { //Propagate out any exceptions
                    throw exc.InnerException;
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

        public int ProcessThread()
        {
            var inst = Context.Next();
            if (inst == null)
                return 0;
            
            return inst.Execute(Context);
        }

        public int ProcessThreadWithOptions(InvokerOptions options)
        {
            var inst = Context.Next();
            if (inst == null)
                return 0;
            
            //Trace execution
            if (options.LogInstructionExecution != InstructionLogging.None)
            {
                LogPreInstruction(options, inst);
            }

            int steps = 0;
            if (options.CollectStats)
            {
                Context.InstructionTimer.Restart();
                steps += inst.Execute(Context);
                Context.InstructionTimer.Stop();

                var st = Context.Stats[(ushort)inst.Op];
                st.count += 1;
                st.duration += Context.InstructionTimer.ElapsedTicks;
                Context.Stats[(ushort)inst.Op] = st;
            }
            else
            {
                steps += inst.Execute(Context);
            }

            if (options.LogInstructionExecution.Has(InstructionLogging.Computes))
            {
                LogPostInstruction(options, inst);
            }
            
            lastInstruction = inst;
            return steps;
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

        private void PrintStats()
        {
            long procTicks = Context.ProcessTimer.ElapsedTicks;
            long totalExecs = Context.Stats.Values.Sum(dc => dc.count);
            long execTicks = Context.Stats.Values.Sum(dc => dc.duration);
            long overheadTicks = procTicks - execTicks;

            TimeSpan totalTime = new TimeSpan(procTicks);
            TimeSpan execTime = new TimeSpan(execTicks);
            TimeSpan overheadTime = new TimeSpan(overheadTicks);
            double overheadPercent =  100.0 * overheadTicks / procTicks;
            double execPercent = 100.0 * execTicks / procTicks;
            string overheadLabel = $"({overheadPercent:#0.###}%) {overheadTime}";
            
            string totalLabel = "    total duration";
            string totalInst = $"{totalExecs}";
            string totalPercent = $"{execPercent:#0.###}%t".PadLeft(8,' ');
            string avgTime = $"{new TimeSpan(execTicks / totalExecs)}/instruction";
            string velocity = $"{totalExecs*1.0/totalTime.TotalMilliseconds:#0.#} inst/ms";
            Console.Error.WriteLine($"Execution Stats:");
            Console.Error.WriteLine($"{totalLabel}: {totalInst}| ({totalPercent}) {execTime} {avgTime} {velocity} overhead:{overheadLabel} total proctime:{totalTime}");
            var orderedStats = Context.Stats
                .Where(bdc => bdc.Value.count != 0)
                .OrderBy(bdc => -bdc.Value.count);
            
            foreach (var (opcode, st) in orderedStats)
            {
                string label = $"{((ByteCode)opcode).GetMnemonic()}".PadLeft(totalLabel.Length, ' ');
                TimeSpan instTime = new TimeSpan(st.duration);
                double percent = 100.0 * st.duration / execTicks;
                string execsLabel = $"{st.count}".PadLeft(totalInst.Length, ' ');
                string percentLabel = $"{percent:#0.###}%e".PadLeft(8,' ');
                Console.Error.WriteLine($"{label}: {execsLabel}| ({percentLabel}) {instTime}");
            }
        }

        public MemoryInstance BindHostMemory((string module, string entity) id, MemoryType memType)
        {
            Store.OpenTransaction();
            var memAddr = AllocateMemory(Store, memType);
            _entityBindings[id] = memAddr;
            Store.CommitTransaction();
            return Store[memAddr];
        }

        public GlobalInstance BindHostGlobal((string module, string entity) id, GlobalType globalType, Value val)
        {
            if (globalType.ContentType != val.Type)
                throw new ArgumentException(
                    $"Global {globalType.ContentType} must be defined with matching type value {val}");
            
            Store.OpenTransaction();
            var globAddr = AllocateGlobal(Store, globalType, val);
            _entityBindings[id] = globAddr;
            Store.CommitTransaction();
            return Store[globAddr];
        }

        public TableInstance BindHostTable((string module, string entity) id, TableType tableType, Value val)
        {
            if (tableType.ElementType.StackType() != val.Type)
                throw new ArgumentException(
                    $"Table {tableType.ElementType} must be defined with matching element type value {val}");
            
            Store.OpenTransaction();
            var tableAddr = AllocateTable(Store, tableType, val);
            _entityBindings[id] = tableAddr;
            Store.CommitTransaction();
            return Store[tableAddr];
        }

        /// <summary>
        /// @Spec 4.5.3.10. Modules Allocation
        /// *We also evaluate globals and elements here, per instantiation
        /// </summary>
        private ModuleInstance AllocateModule(Module module)
        {
            //20. Include Types from module
            var moduleInstance = new ModuleInstance(module);

            //Resolve Imports
            foreach (var import in module.Imports)
            {
                var entityId = (module: import.ModuleName, entity: import.Name);
                switch (import.Desc)
                {
                    case Module.ImportDesc.FuncDesc funcDesc:
                        // @Spec 4.5.3.2. @note: Host Functions must be bound to the environment prior to module instantiation!
                        var funcSig = moduleInstance.Types[funcDesc.TypeIndex];
                        if (GetBoundEntity(entityId) is not FuncAddr funcAddr)
                            throw new NotSupportedException(
                                $"The imported Function was not provided by the environment: {entityId.module}.{entityId.entity} {funcSig.ToNotation()}");
                        var functionInstance = Store[funcAddr];
                        if (functionInstance is FunctionInstance wasmFunc)
                        {
                            wasmFunc.SetName(entityId.entity);
                        }
                        if (!functionInstance.Type.Matches(funcSig))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Function {entityId.module}.{entityId.entity}: expected {funcSig.ToNotation()}, env provided Function {functionInstance.Type.ToNotation()}");
                        //14. external imported addresses first
                        moduleInstance.FuncAddrs.Add(funcAddr);
                        break;
                    case Module.ImportDesc.TableDesc tableDesc:
                        var tableType = tableDesc.TableDef;
                        if (GetBoundEntity(entityId) is not TableAddr tableAddr)
                            throw new NotSupportedException(
                                $"The imported Table was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var tableInstance = Store[tableAddr];
                        if (!tableType.IsCompatibleWith(tableInstance.Type))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Table {entityId.module}.{entityId.entity}: expected {tableType}, env provided Table {tableInstance.Type}");
                        //15. external imported addresses first
                        moduleInstance.TableAddrs.Add(tableAddr);
                        break;
                    case Module.ImportDesc.MemDesc memDesc:
                        var memType = memDesc.MemDef;
                        if (GetBoundEntity(entityId) is not MemAddr memAddr)
                            throw new NotSupportedException(
                                $"The imported Memory was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var memInstance = Store[memAddr];
                        if (!memType.IsCompatibleWith(memInstance.Type))
                            throw new NotSupportedException(
                                $"Type mismatch while importing Memory {entityId.module}.{entityId.entity}: expected {memType}, env provided Memory {memInstance.Type}");
                        //16. external imported addresses first
                        moduleInstance.MemAddrs.Add(memAddr);
                        break;
                    case Module.ImportDesc.GlobalDesc globalDesc:
                        var globalType = globalDesc.GlobalDef;
                        if (GetBoundEntity(entityId) is not GlobalAddr globalAddr)
                            throw new NotSupportedException(
                                $"The imported Global was not provided by the environment: {entityId.module}.{entityId.entity}");
                        var globalInstance = Store[globalAddr];
                        if (globalInstance.Type != globalType)
                            throw new NotSupportedException(
                                $"Type mismatch while importing Global {entityId.module}.{entityId.entity}: expected {globalType}, env provided Global {globalInstance.Type}");
                        //17. external imported addresses first
                        moduleInstance.GlobalAddrs.Add(globalAddr);
                        break;
                }
            }

            //2. Allocate Functions and capture their addresses in the Store
            //8. index ordered function addresses
            foreach (var func in module.Funcs)
            {
                moduleInstance.FuncAddrs.Add(AllocateWasmFunc(Store, func, moduleInstance));
            }

            //3. Allocate Tables and capture their addresses in the Store
            //9. index ordered table addresses
            foreach (var table in module.Tables)
            {
                moduleInstance.TableAddrs.Add(AllocateTable(Store, table, Value.RefNull(table.ElementType)));
            }

            //4. Allocate Memories and capture their addresses in the Store
            //10. index ordered memory addresses
            foreach (var mem in module.Memories)
            {
                moduleInstance.MemAddrs.Add(AllocateMemory(Store, mem));
            }
            //Make the address space permanent
            moduleInstance.MemAddrs.Finalize();

            //@Spec 4.5.4 Step 7
            Frame initFrame = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.GlobalInitializers);
            Context.PushFrame(initFrame);

            //5. Allocate Globals and capture their addresses in the Store
            //11. index ordered global addresses
            foreach (var global in module.Globals)
            {
                var val = EvaluateExpression(global.Initializer);
                if (Context.Frame != initFrame)
                    throw new TrapException($"Call stack was manipulated while initializing globals");
                moduleInstance.GlobalAddrs.Add(AllocateGlobal(Store, global.Type, val));
            }

            Context.PopFrame();
            Frame initFrame2 = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.ElementInitializers);
            Context.PushFrame(initFrame2);

            //6. Allocate Elements
            //12. index ordered element addresses
            foreach (var elem in module.Elements)
            {
                var refs = EvaluateInitializers(elem.Initializers);
                moduleInstance.ElemAddrs.Add(AllocateElement(Store, elem.Type, refs));
            }

            // @Spec 4.5.4 Step 10
            Context.PopFrame();

            //7. Allocate Datas
            //13. index ordered data addresses
            foreach (var data in module.Datas)
            {
                moduleInstance.DataAddrs.Add(AllocateData(Store, data.Init));
            }

            //18. Collect exports
            //Process Exports, keep them, so they can be bound with a module name and imported by later modules.
            HashSet<string> exportedEntities = new();
            foreach (var export in module.Exports)
            {
                if (exportedEntities.Contains(export.Name))
                    throw new InvalidDataException($"Module had multiple exports named {export.Name}");
                exportedEntities.Add(export.Name);
                
                var desc = export.Desc;
                ExternalValue val = desc switch
                {
                    Module.ExportDesc.FuncDesc funcDesc =>
                        new ExternalValue.Function(moduleInstance.FuncAddrs[funcDesc.FunctionIndex]),
                    Module.ExportDesc.TableDesc tableDesc =>
                        new ExternalValue.Table(moduleInstance.TableAddrs[tableDesc.TableIndex]),
                    Module.ExportDesc.MemDesc memDesc =>
                        new ExternalValue.Memory(moduleInstance.MemAddrs[memDesc.MemoryIndex]),
                    Module.ExportDesc.GlobalDesc globalDesc =>
                        new ExternalValue.Global(moduleInstance.GlobalAddrs[globalDesc.GlobalIndex]),
                    _ =>
                        throw new InvalidDataException($"Invalid Export {desc}")
                };
                var exportInstance = new ExportInstance(export.Name, val);
                //19. indexed export addresses
                moduleInstance.Exports.Add(exportInstance);
            }

            //Patch in export names
            var exportedFuncs = moduleInstance.Exports
                .Where(exp => exp.Value is ExternalValue.Function);
            foreach (var export in exportedFuncs)
            {
                var funcDesc = export.Value as ExternalValue.Function;
                var funcAddr = funcDesc!.Address;
                var funcInst = Store[funcAddr];
                if (!funcInst.IsExport)
                {
                    funcInst.SetName(export.Name);
                    funcInst.IsExport = true;
                }
            }

            return moduleInstance;
        }

        /// <summary>
        /// @Spec 4.5.4. Instantiation
        /// </summary>
        public ModuleInstance InstantiateModule(Module module, RuntimeOptions? options = default)
        {
            options ??= new RuntimeOptions();
            try
            {
                //1
                if (!options.SkipModuleValidation)
                    module.ValidateAndThrow();
            }
            catch (ValidationException exc)
            {
                _ = exc;
                throw;
            }

            try
            {
                Store.OpenTransaction();

                ModuleInstance moduleInstance;
                //2, 3, 4 Checks if imports are satisfied
                moduleInstance = AllocateModule(module);

                //12.
                var auxFrame = Context.ReserveFrame(moduleInstance, FunctionType.Empty, FuncIdx.ElementInitialization);
                //13.
                Context.PushFrame(auxFrame);

                //14, 15
                for (int i = 0, l = module.Elements.Length; i < l; ++i)
                {
                    var elem = module.Elements[i];
                    switch (elem.Mode)
                    {
                        case Module.ElementMode.ActiveMode activeMode:
                            var n = elem.Initializers.Length;
                            activeMode.Offset
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const).Immediate(0)
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const).Immediate(n)
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstTableInit>(ExtCode.TableInit)
                                .Immediate(activeMode.TableIndex, (ElemIdx)i)
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstElemDrop>(ExtCode.ElemDrop)
                                .Immediate((ElemIdx)i)
                                .Execute(Context);
                            break;
                        case Module.ElementMode.DeclarativeMode declarativeMode:
                            _ = declarativeMode;
                            Context.InstructionFactory.CreateInstruction<InstElemDrop>(ExtCode.ElemDrop)
                                .Immediate((ElemIdx)i)
                                .Execute(Context);
                            break;
                    }
                }

                //16.
                for (int i = 0, l = module.Datas.Length; i < l; ++i)
                {
                    var data = module.Datas[i];
                    switch (data.Mode)
                    {
                        case Module.DataMode.ActiveMode activeMode:
                            if (activeMode.MemoryIndex != 0)
                                throw new NotSupportedException(
                                    "Module could not be instantiated: Multiple Memories are not supported.");
                            var n = data.Init.Length;
                            activeMode.Offset
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const).Immediate(0)
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstI32Const>(OpCode.I32Const).Immediate(n)
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstMemoryInit>(ExtCode.MemoryInit)
                                .Immediate((DataIdx)i)
                                .Execute(Context);
                            Context.InstructionFactory.CreateInstruction<InstDataDrop>(ExtCode.DataDrop)
                                .Immediate((DataIdx)i)
                                .Execute(Context);
                            break;
                        case Module.DataMode.PassiveMode: //Do nothing
                            break;
                    }
                }


                //17. 
                if (module.StartIndex != FuncIdx.Default)
                {
                    if (!moduleInstance.FuncAddrs.Contains(module.StartIndex))
                        throw new ValidationException("Module StartFunction index was invalid");
                    
                    var startAddr = moduleInstance.FuncAddrs[module.StartIndex];
                    if (!Context.Store.Contains(startAddr))
                        throw new WasmRuntimeException("Module StartFunction address not found in the Store.");

                    moduleInstance.StartFunc = startAddr;

                    //Invoke the function!
                    if (!options.SkipStartFunction)
                    {
                        try
                        {
                            var startInvoker = CreateInvoker<Action>(startAddr);
                            startInvoker();
                        }
                        catch (TrapException)
                        {
                            //see linking.wast: line 412
                            // We're supposed to commit if the start function traps I guess...
                            Store.CommitTransaction();
                            Store.OpenTransaction();
                            throw;
                        }
                    }
                }

                //18.
                if (Context.Frame != auxFrame)
                    throw new InstantiationException("Execution fault in Module Instantiation.");
                //19.
                Context.PopFrame();

                _moduleInstances.Add(moduleInstance);

                return moduleInstance;
            }
            catch (WasmRuntimeException)
            {
                Store.DiscardTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                throw;
            }
            catch (OutOfBoundsTableAccessException)
            {
                //The spec after v1 says to just keep active elements?
                // see linking.wast:264
                Store.CommitTransaction();
                Store.OpenTransaction();
                //Store.DiscardTransaction();

                Context.FlushCallStack();
                throw;
            }
            catch (TrapException)
            {
                Store.DiscardTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                throw;
            }
            catch (NotSupportedException)
            {
                Store.DiscardTransaction();
                Store.OpenTransaction();
                Context.FlushCallStack();
                throw;
            }
            finally
            {
                Store.CommitTransaction();
            }
        }


        /// <summary>
        /// @Spec 4.5.3.1. Functions
        /// </summary>
        private static FuncAddr AllocateWasmFunc(Store store, Module.Function func, ModuleInstance moduleInst)
        {
            return store.AllocateWasmFunction(func, moduleInst);
        }

        /// <summary>
        /// @Spec 4.5.3.2. Host Functions
        /// </summary>
        private static FuncAddr AllocateHostFunc(Store store, (string module, string entity) id, FunctionType funcType, Type delType, Delegate hostFunc)
        {
            return store.AllocateHostFunction(id, funcType, delType, hostFunc);
        }

        /// <summary>
        /// @Spec 4.5.3.3. Tables
        /// </summary>
        private static TableAddr AllocateTable(Store store, TableType tableType, Value refVal)
        {
            var tableInst = new TableInstance(tableType, refVal);
            var tableAddr = store.AddTable(tableInst);
            return tableAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.4. Memories
        /// </summary>
        private static MemAddr AllocateMemory(Store store, MemoryType memType)
        {
            var memInst = new MemoryInstance(memType);
            var memAddr = store.AddMemory(memInst);
            return memAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.5. Globals
        /// </summary>
        private static GlobalAddr AllocateGlobal(Store store, GlobalType globalType, Value val)
        {
            var globalInst = new GlobalInstance(globalType, val);
            var globalAddr = store.AddGlobal(globalInst);
            return globalAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.6. Element segments
        /// </summary>
        private static ElemAddr AllocateElement(Store store, ReferenceType refType, List<Value> refs)
        {
            var elemInst = new ElementInstance(refType, refs);
            var elemAddr = store.AddElement(elemInst);
            return elemAddr;
        }

        /// <summary>
        /// @Spec 4.5.3.7. Data Segments
        /// </summary>
        private static DataAddr AllocateData(Store store, byte[] init)
        {
            var dataInst = new DataInstance(init);
            var dataAddr = store.AddData(dataInst);
            return dataAddr;
        }

        /// <summary>
        /// @Spec 4.5.4. Instantiation
        /// Step 8.1
        /// Execute instructions without gas
        /// </summary>
        private Value EvaluateExpression(Expression ini)
        {
            ini.Execute(Context);
            
            var value = Context.OpStack.PopAny();
            return value;
        }

        private List<Value> EvaluateInitializers(Expression[] inis)
        {
            return inis.Select(EvaluateExpression).ToList();
        }
    }

   
}