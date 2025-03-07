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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core
{

    public partial class Module : IRenderable
    {
        private readonly Dictionary<string, (int line, string instruction)> _pathLineCache = new();

        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            writer.WriteLine($"{indent}(module");
        }

        //Function[100].Expression[34].NumericInst
        public (int line, string instruction) CalculateLine(string validationPath, bool print = false, bool functionRelative = false)
        {
            if (!validationPath.StartsWith("Function["))
                return (-1, "???");
            
            int line = 1;

            string foundInstruction = "";
            if (_pathLineCache.TryGetValue(validationPath, out var result))
            {
                line = result.line;
                foundInstruction = result.instruction;
            }
            else
            {
                //Start of functions
                var parts = validationPath.Split(".");

                IBlockInstruction pointerInst = null!;
                InstructionSequence seq = null!;
                bool addLocals = false;
                string indent = "";
                foreach (var part in parts)
                {
                    indent += " ";
                    //Skip instruction strata
                    if (!part.EndsWith("]"))
                    {
                        line += 1;
                        break;
                    }

                    var regex = new Regex(@"(\w+)\[(\d+)\]");
                    var match = regex.Match(part);
                    if (match.Success)
                    {
                        int index = int.Parse(match.Groups[2].Value);
                        string strata = match.Groups[1].Value;
                        switch (strata)
                        {
                            case "Function":
                            {
                                index -= Imports.Length;
                                for (int i = 0; i < index; ++i)
                                {
                                    if (!functionRelative)
                                    {
                                        line += Funcs[i].Size; //other (func
                                    }
                                }
                                if (index < 0)
                                    return (-1, "???");

                                if (print)
                                    Console.WriteLine($"{indent}Function[{index + Imports.Length}]:{line}");

                                if (Funcs[index].Locals.Length > 0)
                                    addLocals = true;

                                foundInstruction = "func";

                                seq = Funcs[index].Body.Instructions;
                                break;
                            }
                            case "Expr":
                            case "Expression":
                            case "Block":
                            case "If":
                            case "Else":
                            case "Loop":
                            {
                                if (seq == null)
                                    throw new ArgumentException("Validation path was invalid.");

                                //Fast-forward through instructions
                                for (int i = 0; i < index; ++i)
                                {
                                    var inst = seq[i];
                                    if (inst is IBlockInstruction blockInstruction)
                                    {
                                        line += blockInstruction.BlockSize;
                                    }
                                    else
                                    {
                                        line += 1;
                                    }
                                }

                                if (addLocals)
                                {
                                    addLocals = false;
                                    line += 1;
                                }

                                line += 1;

                                if (print)
                                    Console.WriteLine($"{indent}{strata}[{index}]:{line}");

                                var term = seq[index];
                                if (term is IBlockInstruction blTerm)
                                {
                                    pointerInst = blTerm;
                                }

                                foundInstruction = term?.Op.GetMnemonic() ?? "null";
                                break;
                            }
                            case "InstBlock":
                            case "InstLoop":
                            case "InstIf":
                            case "InstElse":
                            {
                                if (pointerInst == null)
                                    {
                                        Console.Error.WriteLine($"Validation path was invalid. {validationPath}");
                                        break;
                                    // throw new ArgumentException("Validation path was invalid.");
                                    }

                                if (print)
                                    Console.WriteLine($"{indent}{strata}[{index}]:{line}");

                                seq = pointerInst.GetBlock(index).Instructions;
                                break;
                            }
                        }
                    }
                }

                _pathLineCache[validationPath] = (line, foundInstruction);
            }

            if (!functionRelative)
            {
                line += 1;              //(module
                line += Types.Count;    //  (type
                line += Imports.Length; //  (import
            }
            return (line, foundInstruction);
        }
    }

    public static class ModuleRenderer
    {
        public const string Indent2Space = "  ";

        public static void RenderWatToStream(Stream output, Module module)
        {
            string indent = "";
            using var writer = new StreamWriter(output, new UTF8Encoding(false), -1, true);

            module.RenderText(writer, module, indent);
            
            indent += Indent2Space;
            //Types
            foreach (var type in module.Types)
            {
                type.RenderText(writer, module, indent);                
            }

            //Imports
            foreach (var import in module.Imports)
            {
                import.RenderText(writer, module, indent);
            }
            
            //Functions
            int idx = module.ImportedFunctions.Count;
            for (int i = 0, l = module.Funcs.Count; i < l; ++i, ++idx)
            {
                RenderFunctionWatToStream(writer, module, (FuncIdx)idx, indent);
            }
            
            //Tables
            foreach (var table in module.Tables)
            {
                table.RenderText(writer, module, indent);
            }
            
            //Memories
            foreach (var mem in module.Memories)
            {
                mem.RenderText(writer, module, indent);
            }
            
            //Globals
            foreach (var glob in module.Globals)
            {
                glob.RenderText(writer, module, indent);
            }
            
            //Exports
            foreach (var exp in module.Exports)
            {
                exp.RenderText(writer, module, indent);
            }
            
            //Start Function
            if (module.StartIndex != FuncIdx.Default)
            {
                var startText = $"(start {module.StartIndex.Value})";
                writer.WriteLine(startText);
            }
            
            //Elements
            foreach (var elem in module.Elements)
            {
                elem.RenderText(writer, module, indent);
            }

            
            //Data
            foreach (var data in module.Datas)
            {
                if (data != module.Datas[^1])
                {
                    data.RenderText(writer, module, indent);
                    writer.WriteLine();
                }
                else
                {
                    data.RenderText(writer, module, indent);
                }
            }
            writer.WriteLine(")");
            
            writer.Flush();
            writer.Close();
        }

        [SuppressMessage("ReSharper.DPA", "DPA0001: Memory allocation issues")]
        private static void RenderFunctionWatToStream(StreamWriter writer, Module module, FuncIdx index, string indent, bool renderStack = false)
        {
            //Skip imports
            int idx = (int)(index.Value - module.ImportedFunctions.Count);
            var func = module.Funcs[idx];
            bool state = func.RenderStack;
            
            func.RenderStack = renderStack;
            func.RenderText(writer, module, indent);
            func.RenderStack = state;
        }

        /// <summary>
        /// Render out the WAT format of this function.
        /// </summary>
        /// <param name="module">The parsed module</param>
        /// <param name="index">The function index to render</param>
        /// <param name="indent"></param>
        /// <param name="renderStack">If true, renderer will put the opstack manipulations in the left hand margin.</param>
        /// <returns>The rendered WAT text.</returns>
        [SuppressMessage("ReSharper.DPA", "DPA0001: Memory allocation issues")]
        public static string RenderFunctionWat(Module module, FuncIdx index, string indent, bool renderStack = false)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), -1, true);
            RenderFunctionWatToStream(writer, module, index, indent, renderStack);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static FuncIdx GetFuncIdx(string path)
        {
            var parts = path.Split(".");
            var regex = new Regex(@"Function\[(\d+)\]");
            var match = regex.Match(parts[0]);
            if (!match.Success)
                return FuncIdx.Default;
            
            return (FuncIdx)uint.Parse(match.Groups[1].Value);
        }

        public static string ChopFunctionId(string path)
        {
            var parts = path.Split(".");
            var regex = new Regex(@"Function\[(\d+)\]");
            var match = regex.Match(parts[0]);
            if (!match.Success)
                throw new ArgumentException("Function was not found in path.");

            return parts[0];
        }
    }
}