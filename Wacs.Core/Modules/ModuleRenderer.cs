using System;
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
        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            writer.WriteLine($"{indent}(module");
        }

        //Function[100].Expression[34].NumericInst
        public (int line, string instruction) CalculateLine(string validationPath, bool print = false, bool functionRelative = false)
        {
            int line = 0;

            if (!functionRelative)
            {
                line += 1;              //(module
                line += Types.Count;    //  (type
                line += Imports.Length; //  (import
            }
            //Start of functions
            var parts = validationPath.Split(".");
            string foundInstruction = "";

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
                        
                            if (print)
                                Console.WriteLine($"{indent}Function[{index+Imports.Length}]:{line}");

                            if (Funcs[index].Locals.Length > 0)
                                addLocals = true;

                            foundInstruction = "func";

                            seq = Funcs[index].Body.Instructions;
                            break;
                        }
                        case "Expression":
                        case "Block":
                        {
                            if (seq == null)
                                throw new ArgumentException("Validation path was invalid.");
                        
                            //Fast-forward through instructions
                            for (int i = 0; i < index; ++i)
                            {
                                var inst = seq[i];
                                if (inst is IBlockInstruction blockInstruction)
                                {
                                    line += blockInstruction.Size;
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
                            foundInstruction = term.Op.GetMnemonic();
                            break;
                        }
                        case "InstBlock":
                        case "InstIf":
                        case "InstLoop":
                        {
                            if (pointerInst == null)
                                throw new ArgumentException("Validation path was invalid.");
                        
                            if (print)
                                Console.WriteLine($"{indent}{strata}[{index}]:{line}");
                        
                            seq = pointerInst.GetBlock(index);
                            break;
                        }
                    }
                }
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