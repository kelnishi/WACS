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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Attributes;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        public List<Function> Funcs { get; internal set; } = new();

        public List<Function> ValidationFuncs => ImportedFunctions.Concat(Funcs).ToList();

        /// <summary>
        /// @Spec 2.5.3 Functions
        /// </summary>
        public class Function : IRenderable
        {
            public FuncIdx Index;

            public bool ElementDeclared = false;

            public bool IsFullyDeclared(IWasmValidationContext ctx) => 
                ElementDeclared || Index.Value < ctx.FunctionIndex.Value;

            public bool IsImport = false;
            public string Id { get; set; } = "";

            //Function Section only parses the type indices
            public TypeIdx TypeIndex { get; internal set; }

            //Locals and Body get parsed in the Code Section
            public ValType[] Locals { get; internal set; } = null!;
            public Expression Body { get; internal set; } = null!;

            public int Size => (Locals.Length > 0?1:0) + Body.Size;

            public bool RenderStack { get; set; } = false;

            public void RenderText(StreamWriter writer, Module module, string indent)
            {
                var id = string.IsNullOrWhiteSpace(Id) ? $" (;{Index.Value};)" : $" (;{Id};)";
                var type = $" (type {TypeIndex.Value})";
                var functionType = (FunctionType)module.Types[(int)TypeIndex.Value];
                var param = functionType.ParameterTypes.Arity > 0
                    ? functionType.ParameterTypes.ToParameters()
                    : "";
                var result = functionType.ResultType.Arity > 0
                    ? functionType.ResultType.ToResults()
                    : "";
                
                var head = $"{indent}(func{id}{type}{param}{result}";
                
                writer.Write(head);
                indent += ModuleRenderer.Indent2Space;
                if (Locals.Length > 0)
                {
                    var localtypes = string.Join(" ", Locals.Select(v => v.ToWat()));
                    var locals = $"{indent}(local {localtypes})";
                    writer.WriteLine();
                    writer.Write(locals);
                }

                var fakeContext = new FakeContext(module, this);
                StackRenderer stackRenderer = new(null, RenderStack, context:fakeContext);
                fakeContext.PushControlFrame(OpCode.Func, functionType);
                RenderInstructions(writer, indent, 0, module, Body.Instructions, stackRenderer);
                fakeContext.PopControlFrame();
                writer.WriteLine(")");
            }

            private void RenderInstructions(StreamWriter writer, string indent, int depth, Module module, InstructionSequence seq, StackRenderer stackRenderer)
            {
                foreach (var inst in seq)
                {
                    switch (inst)
                    {
                        case InstElse:
                        case InstEnd:
                            //Skip and handle in block rendering
                            break;
                        case IBlockInstruction blockInst:
                        {
                            depth += 1;
                            stackRenderer.FakeContext.LastEvent = "[";
                            var mnemonic = inst.Op.GetMnemonic();
                            var funcType = ComputeBlockType(blockInst.BlockType, module);
                            var blockParams = funcType.ParameterTypes.Arity > 0
                                ? funcType.ParameterTypes.ToParameters()
                                : "";
                            var blockResults = funcType.ResultType.Arity > 0
                                ? funcType.ResultType.ToResults()
                                : "";
                            var label = $"  ;; label = @{depth}";
                            var instText = $"{stackRenderer}{indent}{mnemonic}{blockParams}{blockResults}{label}";
                            writer.WriteLine();
                            writer.Write(instText);

                            var blockIndent = indent + ModuleRenderer.Indent2Space;
                        
                            for (int b = 0; b < blockInst.Count; ++b)
                            {
                                stackRenderer.FakeContext.LastEvent = "[";
                                stackRenderer.ProcessInstruction(inst);
                                
                                var blockSeq = blockInst.GetBlock(b).Instructions;
                                stackRenderer.FakeContext.DummyContext.Frame.ForceLabels(depth+1);
                                RenderInstructions(writer, blockIndent, depth, module, blockSeq, stackRenderer.SubRenderer());

                                var lastInst = blockSeq.LastInstruction;
                                if (InstructionBase.IsElseOrEnd(lastInst))
                                {
                                    stackRenderer.FakeContext.LastEvent = InstructionBase.IsEnd(lastInst) ? "[" : "][";
                                    stackRenderer.ProcessInstruction(inst);
                                    stackRenderer.FakeContext.DummyContext.Frame.ForceLabels(depth);
                                    var endLabel = $" (;< @{depth} ;)";
                                    var subText = $"{stackRenderer}{indent}{lastInst.RenderText(stackRenderer.FakeContext.DummyContext)}{endLabel}";
                                    writer.WriteLine();
                                    writer.Write(subText);
                                }
                            }

                            depth -= 1;
                            break;
                        }
                        default:
                        {
                            stackRenderer.ProcessInstruction(inst);
                            var instText = $"{stackRenderer}{indent}{inst.RenderText(stackRenderer.FakeContext.DummyContext)}";
                            writer.WriteLine();
                            writer.Write(instText);
                            break;
                        }
                    }
                }
            }

            //TODO: This doesn't really work...
            private FunctionType ComputeBlockType(ValType type, Module module) =>
                type switch
                {
                    ValType.Empty => new FunctionType(ResultType.Empty, ResultType.Empty),
                    ValType.I32 => new FunctionType(ResultType.Empty, new ResultType(ValType.I32)),
                    ValType.F32 => new FunctionType(ResultType.Empty, new ResultType(ValType.F32)),
                    ValType.F64 => new FunctionType(ResultType.Empty, new ResultType(ValType.F64)),
                    ValType.I64 => new FunctionType(ResultType.Empty, new ResultType(ValType.I64)),
                    ValType.V128 => new FunctionType(ResultType.Empty, new ResultType(ValType.V128)),
                    ValType.FuncRef => new FunctionType(ResultType.Empty, new ResultType(ValType.FuncRef)),
                    ValType.ExternRef => new FunctionType(ResultType.Empty, new ResultType(ValType.ExternRef)),
                    //TODO: not so much...
                    _ => module.Types[(int)((TypeIdx)(uint)type).Value]
                };

            /// <summary>
            /// @Spec 3.4.1. Functions
            /// </summary>
            public class Validator : AbstractValidator<Function>
            {
                public Validator()
                {
                    // @Spec 3.4.1.1
                    RuleFor(func => func.TypeIndex)
                        .Must((_, index, ctx) =>
                            ctx.GetValidationContext().Types.Contains(index));
                    RuleFor(func => func)
                        .Custom((func, ctx) =>
                        {
                            var vContext = ctx.GetValidationContext();
                            
                            var types = vContext.Types;
                            if (!types.Contains(func.TypeIndex))
                            {
                                throw new ValidationException($"Function.TypeIndex not within Module.Types");
                            }
                            
                            if (func.IsImport)
                                return;
                            
                            if (func.Locals.Length > vContext.Attributes.MaxFunctionLocals)
                                throw new ValidationException(
                                    $"Function[{func.Index}] locals count {func.Locals.Length} exceeds maximum allowed {vContext.Attributes.MaxFunctionLocals}");

                            foreach (var (localType,index) in func.Locals.Select((l,i)=>(l,i)))
                            {
                                if (!localType.Validate(vContext.Types))
                                    throw new ValidationException($"Function[{func.Index}] Local[{index}] had invalid type:{localType}");
                            }
                            
                            var type = types[func.TypeIndex];
                            var funcType = type.Expansion as FunctionType;
                            if (funcType is null)
                                throw new ValidationException($"Function[{func.Index}] type {type} is not a FuncType.");

                            foreach (var (paramType, index) in funcType.ParameterTypes.Types.Select((t, i) => (t, i)))
                            {
                                if (!paramType.Validate(vContext.Types))
                                    throw new ValidationException($"Function[{func.Index}] Parameter[{index}] had invalid type:{paramType}");
                            }
                            foreach (var (resType, index) in funcType.ResultType.Types.Select((t, i) => (t, i)))
                            {
                                if (!resType.Validate(vContext.Types))
                                    throw new ValidationException($"Function[{func.Index}] Result[{index}] had invalid type:{resType}");
                            }                            
                            
                            vContext.FunctionIndex = func.Index;
                            vContext.SetExecFrame(funcType, func.Locals);
                            
                            //*Expression Validator also validates result types
                            var exprValidator = new Expression.Validator(funcType.ResultType);
                            var subcontext = vContext.PushSubContext(func.Body);
                            try
                            {
                                var validationResult = exprValidator.Validate(subcontext);
                                if (!validationResult.IsValid)
                                {
                                    foreach (var failure in validationResult.Errors)
                                    {
                                        // Map the child validation failures to the parent context
                                        // Adjust the property name to reflect the path to the child property
                                        var propertyName = $"{ctx.PropertyPath}.{failure.PropertyName}";
                                        ctx.AddFailure(propertyName, failure.ErrorMessage);
                                    }
                                }
                            }
                            catch (ValidationException exc)
                            {
                                var message = $"{exc.Message}";
                                ctx.AddFailure(message);
                                throw new ValidationException(message);
                            }
                            vContext.PopValidationContext();
                        });
                }
            }
        }
    }

    public static partial class BinaryModuleParser
    {
        private static Module.Function ParseIndex(BinaryReader reader) =>
            new() {
                TypeIndex = (TypeIdx)reader.ReadLeb128_u32(),
            };

        /// <summary>
        /// @Spec 5.5.6 Function Section
        /// </summary>
        private static Module.Function[] ParseFunctionSection(BinaryReader reader) =>
            reader.ParseVector(ParseIndex);
    }
}