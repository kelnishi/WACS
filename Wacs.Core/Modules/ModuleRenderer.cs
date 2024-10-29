using System;
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
        public int CalculateLine(string validationPath, bool print, out string instruction)
        {
            int line = 2;
            line += Types.Count;
            line += Imports.Length;
            //Start of functions
            var parts = validationPath.Split(".");
            instruction = "";

            IBlockInstruction pointerInst = null;
            InstructionSequence seq = null;
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
                                line += Funcs[i].Size;
                            }
                        
                            if (print)
                                Console.WriteLine($"{indent}Function[{index+Imports.Length}]:{line}");

                            if (Funcs[index].Locals.Length > 0)
                                addLocals = true;

                            instruction = "func";

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
                            instruction = term.Op.GetMnemonic();
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

            return line;
        }
    }

    public static class ModuleRenderer
    {
        public const string Indent2Space = "  ";

        public static void RenderWatToStream(Stream output, Module module)
        {
            string indent = "";
            using var writer = new StreamWriter(output, new UTF8Encoding(true), -1, true);

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
            foreach (var func in module.Funcs)
            {
                func.RenderText(writer, module, indent);
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
        }
    }
}