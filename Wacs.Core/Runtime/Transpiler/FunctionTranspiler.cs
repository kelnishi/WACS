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
        public static void TranspileFunction(FunctionInstance function)
        {
            var expression = function.Definition.Body;
            var newSeq = OptimizeSequence(expression.Instructions);
            function.Body = new Expression(newSeq, false);
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
                        var newBlockSeq = OptimizeSequence(instBlock.GetBlock(0));
                        var newBlock = new InstBlock().Immediate(instBlock.Type, newBlockSeq);
                        stack.Push(newBlock);
                        break;
                    case InstLoop instLoop:
                        var newLoopSeq = OptimizeSequence(instLoop.GetBlock(0));
                        var newLoop = new InstLoop().Immediate(instLoop.Type, newLoopSeq);
                        stack.Push(newLoop);
                        break;
                    case InstIf instIf:
                        var newIfSeq = OptimizeSequence(instIf.GetBlock(0));
                        var newElseSeq = OptimizeSequence(instIf.GetBlock(1));
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
                case IValueConsumer<uint> uintConsumer: return BindU32(inst, stack, uintConsumer);
                case IValueConsumer<int> intConsumer: return BindI32(inst, stack, intConsumer);
                case IValueConsumer<int,int> intConsumer: return BindI32I32(inst, stack, intConsumer);
                case IValueConsumer<uint,uint> uintConsumer: return BindU32U32(inst, stack, uintConsumer);
                case IValueConsumer<uint,int> intConsumer: return BindU32I32(inst, stack, intConsumer);
                
                case IValueConsumer<float> floatConsumer: return BindF32(inst, stack, floatConsumer);
                case IValueConsumer<double> doubleConsumer: return BindF64(inst, stack, doubleConsumer);
                
                default:
                    Console.WriteLine($"Could not optimize {inst}");
                    return inst;
            }
        }

        private static IInstruction BindAnyValue(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<Value> valueConsumer)
        {
            if (stack.Count < 1) return inst;
            var top = stack.Pop();
            if (valueConsumer is INodeComputer<Value> valueComputer) {
                switch (top)
                {
                    case ITypedValueProducer<Value> valProducer:
                        return new InstAggregateValue<Value>(valProducer, ValType.Nil, valueComputer);
                    case ITypedValueProducer<int> intProducer:
                        return new InstAggregateValue<int>(intProducer, ValType.I32, valueComputer);
                    case ITypedValueProducer<uint> uintProducer:
                        return new InstAggregateValue<uint>(uintProducer, ValType.I32, valueComputer);
                    case ITypedValueProducer<long> longProducer:
                        return new InstAggregateValue<long>(longProducer, ValType.I64, valueComputer);
                    case ITypedValueProducer<ulong> ulongProducer:
                        return new InstAggregateValue<ulong>(ulongProducer, ValType.I64, valueComputer);
                    case ITypedValueProducer<float> floatProducer:
                        return new InstAggregateValue<float>(floatProducer, ValType.F32, valueComputer);
                    case ITypedValueProducer<double> doubleProducer:
                        return new InstAggregateValue<double>(doubleProducer, ValType.F64, valueComputer);
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
                    case INodeComputer<uint> uintComputer:
                        return new InstAggregate1_0<uint>(uintProducer, uintComputer);
                    case INodeComputer<uint, uint> uintComputer:
                        return new InstAggregate1_1<uint, uint>(uintProducer, uintComputer);
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
                    case INodeComputer<int> intComputer:
                        return new InstAggregate1_0<int>(intProducer, intComputer);
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
        
        private static IInstruction BindF32(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<float> floatConsumer)
        {
            var top = stack.Pop();
            var floatProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<float>(p),
                ITypedValueProducer<float> p => p,
                _ => null
            };
            if (floatProducer != null) {
                switch (floatConsumer) {
                    case INodeComputer<float> floatComputer:
                        return new InstAggregate1_0<float>(floatProducer, floatComputer);
                    case INodeComputer<float, float> floatComputer: 
                        return new InstAggregate1_1<float, float>(floatProducer, floatComputer);
                }
            }
            stack.Push(top);
            return inst;
        }
        
        private static IInstruction BindF64(IOptimizationTarget inst, Stack<IInstruction> stack, IValueConsumer<double> doubleConsumer)
        {
            var top = stack.Pop();
            var doubleProducer = top switch {
                ITypedValueProducer<Value> p => new UnwrapValue<double>(p),
                ITypedValueProducer<double> p => p,
                _ => null
            };
            if (doubleProducer != null) {
                switch (doubleConsumer) {
                    case INodeComputer<double> doubleComputer:
                        return new InstAggregate1_0<double>(doubleProducer, doubleComputer);
                    case INodeComputer<double, double> doubleComputer: 
                        return new InstAggregate1_1<double, double>(doubleProducer, doubleComputer);
                }
            }
            stack.Push(top);
            return inst;
        }
        
    }
}