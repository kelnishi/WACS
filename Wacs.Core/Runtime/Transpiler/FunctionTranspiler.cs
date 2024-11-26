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
using System.Linq;
using System.Reflection;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Transpiler
{
    public static class FunctionTranspiler
    {
        /// <summary>
        /// Transpiling a function will walk the instruction sequences and link their execution functions
        /// into an (expression) tree. This is similar to Linq Expression Trees, except in our case, we're
        /// passing values around raw without boxing/unboxing when possible.
        /// Instructions are tagged with generic interfaces to indicate that they consume/produce values.
        /// Links are only made between adjacent instructions, so call/block/br like instructions will interrupt
        /// linking. The linked instructions are rolled up into super-instructions (InstAggregates) and are dispatched
        /// as single operations by the runtime. For cases I did not explicitly handle, the instructions will be
        /// copied over, as-is. 
        ///
        /// The goal for super-instructions is to bypass the OpStack. Since our VM is (usually) running in the CLR,
        /// we'd be incurring function calls for stack operations anyway. By directly linking execution functions
        /// we are in-effect lowering these callstack invocations into the CLR where they are highly optimized and
        /// (relatively) closer to the metal. Bypassing the OpStack also allows the CLR to make use of hardware
        /// registers instead of an in-memory value stack for loading parameters. It also means that we are not
        /// using Wacs.Core.Runtime.Value (our runtime's value box; 20bytes!), improving cache coherency
        /// and reducing processing overhead.
        /// </summary>
        /// <param name="function"></param>
        public static void TranspileFunction(FunctionInstance function)
        {
            var expression = function.Definition.Body;
            var newSeq = OptimizeSequence(expression.Instructions);
            function.SetBody(new Expression(function.Type.ResultType.Arity, newSeq, false));
        }

        private static InstructionSequence OptimizeSequence(InstructionSequence seq)
        {
            if (seq.Count == 0)
                return InstructionSequence.Empty;
            
            Stack<IInstruction> stack = new(seq.Count);

            foreach (var inst in seq)
            {
                switch (inst)
                {
                    case InstBlock instBlock:
                        var newBlockSeq = OptimizeSequence(instBlock.GetBlock(0).Instructions);
                        var newBlock = new InstBlock().Immediate(instBlock.Type, newBlockSeq);
                        stack.Push(newBlock);
                        break;
                    case InstLoop instLoop:
                        var newLoopSeq = OptimizeSequence(instLoop.GetBlock(0).Instructions);
                        var newLoop = new InstLoop().Immediate(instLoop.Type, newLoopSeq);
                        stack.Push(newLoop);
                        break;
                    case InstIf instIf:
                        var newIfSeq = OptimizeSequence(instIf.GetBlock(0).Instructions);
                        var newElseSeq = OptimizeSequence(instIf.GetBlock(1).Instructions);
                        var newIf = new InstIf().Immediate(instIf.Type, newIfSeq, newElseSeq);
                        stack.Push(newIf);
                        break;
                    case InstLocalTee instTee:
                        //Split into local.set/local.get
                        var newSet = new InstLocalSet().Immediate(instTee.GetIndex()) as IOptimizationTarget;
                        var newGet = new InstLocalGet().Immediate(instTee.GetIndex());
                        if (newSet != null)
                        {
                            var setInst = OptimizeInstruction(newSet, stack);
                            stack.Push(setInst);
                            stack.Push(newGet);
                            break;
                        }
                        stack.Push(inst);
                        break;
                    case IOptimizationTarget target:
                        var newInst = OptimizeInstruction(target, stack);
                        stack.Push(newInst);
                        break;
                    default:
                        stack.Push(inst);
                        break;
                }
            }
            
            //Attach the new optimized expression
            var newSeq = new InstructionSequence(stack.Reverse().ToList());
            return newSeq;
        }

        private static IInstruction OptimizeInstruction(IOptimizationTarget inst, Stack<IInstruction> stack)
        {
            switch (inst)
            {
                case IValueConsumer<Value> valueConsumer: return BindAnyValue(inst, stack, valueConsumer);
                
                case IValueConsumer<int> intConsumer: return BindI32(inst, stack, intConsumer);
                case IValueConsumer<int,int> intConsumer: return BindI32I32(inst, stack, intConsumer);
                case IValueConsumer<uint> uintConsumer: return BindU32(inst, stack, uintConsumer);
                case IValueConsumer<uint,uint> uintConsumer: return BindU32U32(inst, stack, uintConsumer);
                
                case IValueConsumer<uint,int> intConsumer: return BindU32I32(inst, stack, intConsumer);
                
                case IValueConsumer<long> longConsumer: return BindI64(inst, stack, longConsumer);
                case IValueConsumer<long,long> longConsumer: return BindI64I64(inst, stack, longConsumer);
                case IValueConsumer<ulong> longConsumer: return BindU64(inst, stack, longConsumer);
                case IValueConsumer<ulong,ulong> ulongConsumer: return BindU64U64(inst, stack, ulongConsumer);
                
                case IValueConsumer<ulong,long> longConsumer: return BindU64I64(inst, stack, longConsumer);
                
                case IValueConsumer<float> floatConsumer: return BindF32(inst, stack, floatConsumer);
                case IValueConsumer<float,float> floatConsumer: return BindF32F32(inst, stack, floatConsumer);
                
                case IValueConsumer<double> doubleConsumer: return BindF64(inst, stack, doubleConsumer);
                case IValueConsumer<double,double> doubleConsumer: return BindF64F64(inst, stack, doubleConsumer);
                
                //Memory Store
                case IValueConsumer<uint,Value> valueConsumer: return BindU32Value(inst, stack, valueConsumer);
                
                default: return inst;
            }
        }

        private static IInstruction BindAnyValue(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<Value> valueConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            if (valueConsumer is INodeConsumer<Value> nodeConsumer) {
                switch (top)
                {
                    case ITypedValueProducer<Value> valProducer:
                        return new InstAggregateValue<Value>(valProducer, ValType.Nil, nodeConsumer);
                    case ITypedValueProducer<int> intProducer:
                        return new InstAggregateValue<int>(intProducer, ValType.I32, nodeConsumer);
                    case ITypedValueProducer<uint> uintProducer:
                        return new InstAggregateValue<uint>(uintProducer, ValType.I32, nodeConsumer);
                    case ITypedValueProducer<long> longProducer:
                        return new InstAggregateValue<long>(longProducer, ValType.I64, nodeConsumer);
                    case ITypedValueProducer<ulong> ulongProducer:
                        return new InstAggregateValue<ulong>(ulongProducer, ValType.I64, nodeConsumer);
                    case ITypedValueProducer<float> floatProducer:
                        return new InstAggregateValue<float>(floatProducer, ValType.F32, nodeConsumer);
                    case ITypedValueProducer<double> doubleProducer:
                        return new InstAggregateValue<double>(doubleProducer, ValType.F64, nodeConsumer);
                }
            }
            stack.Push(top);
            return inst;
        }

        private static IInstruction BindU32(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<uint> uintConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            var uintProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<uint>(p),
                ITypedValueProducer<int> p => new CastToU32<int>(p),
                ITypedValueProducer<uint> p => p,
                _ => null
            };
            if (uintProducer != null) {
                switch (uintConsumer) {
                    case INodeConsumer<uint> nodeConsumer:
                        return new InstAggregate1_0<uint>(uintProducer, nodeConsumer);
                    case INodeComputer<uint, uint> uintComputer:
                        return new InstAggregate1_1<uint, uint>(uintProducer, uintComputer);
                    case INodeComputer<uint, ulong> uintComputer:
                        return new InstAggregate1_1<uint, ulong>(uintProducer, uintComputer);
                    //MemoryLoad
                    case INodeComputer<uint, Value> uintComputer:
                        return new InstAggregate1_1<uint, Value>(uintProducer, uintComputer);
                }
            }
            stack.Push(top);
            return inst;
        }

        private static IInstruction BindI32(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<int> intConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            var intProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<int>(p),
                ITypedValueProducer<uint> p => new CastToI32<uint>(p),
                ITypedValueProducer<int> p => p,
                _ => null
            };
            if (intProducer != null) {
                switch (intConsumer) {
                    case INodeConsumer<int> nodeConsumer:
                        return new InstAggregate1_0<int>(intProducer, nodeConsumer);
                    case INodeComputer<int, int> intComputer: 
                        return new InstAggregate1_1<int, int>(intProducer, intComputer);
                }
            }
            stack.Push(top);
            return inst;
        }
        
        private static IInstruction BindI32I32(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<int,int> intConsumer)
        {
            if (stack.Count < 2) return inst;
            
            var i2 = stack.Pop();
            var i1 = stack.Pop();
            
            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<int>(p),
                ITypedValueProducer<uint> p => new CastToI32<uint>(p),
                ITypedValueProducer<int> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<int>(p),
                ITypedValueProducer<uint> p => new CastToI32<uint>(p),
                ITypedValueProducer<int> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (intConsumer) {
                    case INodeComputer<int,int,int> intComputer:
                        return new InstAggregate2_1<int, int, int>(i1Producer, i2Producer, intComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        private static IInstruction BindU32U32(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<uint,uint> intConsumer)
        {
            if (stack.Count < 2) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Pop();

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<uint>(p),
                ITypedValueProducer<int> p => new CastToU32<int>(p),
                ITypedValueProducer<uint> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<uint>(p),
                ITypedValueProducer<int> p => new CastToU32<int>(p),
                ITypedValueProducer<uint> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (intConsumer) {
                    case INodeComputer<uint, uint, uint> uintComputer:
                        return new InstAggregate2_1<uint, uint, uint>(i1Producer, i2Producer, uintComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        private static IInstruction BindU32I32(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<uint,int> intConsumer)
        {
            if (stack.Count < 2) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Pop();

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<int>(p),
                ITypedValueProducer<uint> p => new CastToI32<uint>(p),
                ITypedValueProducer<int> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<uint>(p),
                ITypedValueProducer<int> p => new CastToU32<int>(p),
                ITypedValueProducer<uint> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (intConsumer) {
                    case INodeComputer<uint, int, uint> uintComputer:
                        return new InstAggregate2_1<uint, int, uint>(i1Producer, i2Producer, uintComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        private static IInstruction BindU64(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<ulong> ulongConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            var ulongProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<ulong>(p),
                ITypedValueProducer<long> p => new CastToU64<long>(p),
                ITypedValueProducer<ulong> p => p,
                _ => null
            };
            if (ulongProducer != null) {
                switch (ulongConsumer) {
                    case INodeConsumer<ulong> nodeConsumer:
                        return new InstAggregate1_0<ulong>(ulongProducer, nodeConsumer);
                    case INodeComputer<ulong, ulong> ulongComputer:
                        return new InstAggregate1_1<ulong, ulong>(ulongProducer, ulongComputer);
                    //MemoryLoad
                    case INodeComputer<ulong, Value> ulongComputer:
                        return new InstAggregate1_1<ulong, Value>(ulongProducer, ulongComputer);
                }
            }
            stack.Push(top);
            return inst;
        }

        private static IInstruction BindI64(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<long> longConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            var longProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<long>(p),
                ITypedValueProducer<ulong> p => new CastToI64<ulong>(p),
                ITypedValueProducer<long> p => p,
                _ => null
            };
            if (longProducer != null) {
                switch (longConsumer) {
                    case INodeConsumer<long> nodeConsumer:
                        return new InstAggregate1_0<long>(longProducer, nodeConsumer);
                    case INodeComputer<long, long> longComputer: 
                        return new InstAggregate1_1<long, long>(longProducer, longComputer);
                    case INodeComputer<long, int> longComputer: 
                        return new InstAggregate1_1<long, int>(longProducer, longComputer);
                }
            }
            stack.Push(top);
            return inst;
        }
        
        private static IInstruction BindI64I64(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<long,long> longConsumer)
        {
            if (stack.Count < 2) return inst;
            
            var i2 = stack.Pop();
            var i1 = stack.Pop();
            
            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<long>(p),
                ITypedValueProducer<ulong> p => new CastToI64<ulong>(p),
                ITypedValueProducer<long> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<long>(p),
                ITypedValueProducer<ulong> p => new CastToI64<ulong>(p),
                ITypedValueProducer<long> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (longConsumer) {
                    case INodeComputer<long,long,long> longComputer:
                        return new InstAggregate2_1<long, long, long>(i1Producer, i2Producer, longComputer);
                    case INodeComputer<long,long,int> longComputer:
                        return new InstAggregate2_1<long, long, int>(i1Producer, i2Producer, longComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        private static IInstruction BindU64U64(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<ulong,ulong> longConsumer)
        {
            if (stack.Count < 2) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Pop();

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<ulong>(p),
                ITypedValueProducer<long> p => new CastToU64<long>(p),
                ITypedValueProducer<ulong> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<ulong>(p),
                ITypedValueProducer<long> p => new CastToU64<long>(p),
                ITypedValueProducer<ulong> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (longConsumer) {
                    case INodeComputer<ulong, ulong, ulong> ulongComputer:
                        return new InstAggregate2_1<ulong, ulong, ulong>(i1Producer, i2Producer, ulongComputer);
                    case INodeComputer<ulong, ulong, int> ulongComputer:
                        return new InstAggregate2_1<ulong, ulong, int>(i1Producer, i2Producer, ulongComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        private static IInstruction BindU64I64(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<ulong,long> longConsumer)
        {
            if (stack.Count < 2) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Pop();

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<long>(p),
                ITypedValueProducer<ulong> p => new CastToI64<ulong>(p),
                ITypedValueProducer<long> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<ulong>(p),
                ITypedValueProducer<long> p => new CastToU64<long>(p),
                ITypedValueProducer<ulong> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (longConsumer) {
                    case INodeComputer<ulong, long, ulong> ulongComputer:
                        return new InstAggregate2_1<ulong, long, ulong>(i1Producer, i2Producer, ulongComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        
        private static IInstruction BindU32Value(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<uint,Value> intConsumer)
        {
            if (stack.Count < 1) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Count > 0 ? stack.Pop() : null;

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => p,
                ITypedValueProducer<int> p => new WrapValue<int>(p),
                ITypedValueProducer<uint> p => new WrapValue<uint>(p),
                ITypedValueProducer<long> p => new WrapValue<long>(p),
                ITypedValueProducer<ulong> p => new WrapValue<ulong>(p),
                ITypedValueProducer<float> p => new WrapValue<float>(p),
                ITypedValueProducer<double> p => new WrapValue<double>(p),
                ITypedValueProducer<V128> p => new WrapValue<V128>(p),
                _ => null,
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<uint>(p),
                ITypedValueProducer<int> p => new CastToU32<int>(p),
                ITypedValueProducer<uint> p => p,
                _ => null
            };
            
            if (i2Producer != null)
            {
                if (i1Producer == null)
                {
                    i1Producer = new CastToU32<int>(new InstStackProducer<int>());
                    if (i1 != null) stack.Push(i1);
                    i1 = null;
                }
                
                switch (intConsumer) {
                    case INodeConsumer<uint, Value> nodeConsumer:
                        return new InstAggregate2_0<uint, Value>(i1Producer, i2Producer, nodeConsumer);
                }

            }
            
            if (i1 != null) stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        private static IInstruction BindF32(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<float> floatConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            var floatProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<float>(p),
                ITypedValueProducer<float> p => p,
                _ => null
            };
            if (floatProducer != null) {
                switch (floatConsumer) {
                    case INodeConsumer<float> nodeConsumer:
                        return new InstAggregate1_0<float>(floatProducer, nodeConsumer);
                    case INodeComputer<float, float> floatComputer: 
                        return new InstAggregate1_1<float, float>(floatProducer, floatComputer);
                }
            }
            stack.Push(top);
            return inst;
        }
        
        private static IInstruction BindF32F32(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<float,float> intConsumer)
        {
            if (stack.Count < 2) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Pop();

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<float>(p),
                ITypedValueProducer<float> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<float>(p),
                ITypedValueProducer<float> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (intConsumer) {
                    case INodeComputer<float,float,float> floatComputer:
                        return new InstAggregate2_1<float,float,float>(i1Producer, i2Producer, floatComputer);
                    case INodeComputer<float,float,int> floatComputer:
                        return new InstAggregate2_1<float,float,int>(i1Producer, i2Producer, floatComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
        private static IInstruction BindF64(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<double> doubleConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            var doubleProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<double>(p),
                ITypedValueProducer<double> p => p,
                _ => null
            };
            if (doubleProducer != null) {
                switch (doubleConsumer) {
                    case INodeConsumer<double> nodeConsumer:
                        return new InstAggregate1_0<double>(doubleProducer, nodeConsumer);
                    case INodeComputer<double, double> doubleComputer: 
                        return new InstAggregate1_1<double, double>(doubleProducer, doubleComputer);
                }
            }
            stack.Push(top);
            return inst;
        }
        
        private static IInstruction BindF64F64(IInstruction inst, Stack<IInstruction> stack, IValueConsumer<double,double> intConsumer)
        {
            if (stack.Count < 2) return inst;
            var i2 = stack.Pop();
            var i1 = stack.Pop();

            var i2Producer = i2 switch {
                ITypedValueProducer<Value> p => new UnwrapValue<double>(p),
                ITypedValueProducer<double> p => p,
                _ => null
            };
            var i1Producer = i1 switch
            {
                ITypedValueProducer<Value> p => new UnwrapValue<double>(p),
                ITypedValueProducer<double> p => p,
                _ => null
            };

            if (i1Producer != null && i2Producer != null)
            {
                switch (intConsumer) {
                    case INodeComputer<double,double,double> doubleComputer:
                        return new InstAggregate2_1<double,double,double>(i1Producer, i2Producer, doubleComputer);
                    case INodeComputer<double,double,int> doubleComputer:
                        return new InstAggregate2_1<double,double,int>(i1Producer, i2Producer, doubleComputer);
                }
            }
            
            stack.Push(i1);
            stack.Push(i2);
            return inst;
        }
        
    }
}