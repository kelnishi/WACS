// Copyright 2024 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;

namespace Wacs.Core
{
    /// <summary>
    /// Maps FluentValidation property paths
    /// (<c>Function[N].Expression[M].Block[K]…</c>) to a line number
    /// in the canonical WAT rendering of this module, plus the mnemonic
    /// of the instruction at that path. The CLI's <c>wacs run</c>
    /// validation-error reporter uses this to point users at the
    /// offending source line.
    ///
    /// <para>Cache survives the lifetime of the <see cref="Module"/>
    /// instance — validation typically generates a small number of
    /// repeated paths during a single error-rendering pass.</para>
    ///
    /// <para>Lifted out of the old <c>ModuleRenderer</c> as part of the
    /// consolidation onto <see cref="Wacs.Core.Text.TextModuleWriter"/>.</para>
    /// </summary>
    public partial class Module
    {
        private readonly Dictionary<string, (int line, string instruction)> _pathLineCache = new();

        private static readonly Regex IndexedTokenRegex =
            new(@"(\w+)\[(\d+)\]", RegexOptions.Compiled);

        /// <summary>
        /// Walk a validation path token-by-token, counting WAT lines
        /// per consumed instruction, and return the (line, mnemonic)
        /// pair for the addressed element. When
        /// <paramref name="functionRelative"/> is true the count is
        /// reckoned from the function's <c>(func …)</c> opener
        /// instead of the start of the <c>(module …)</c>.
        /// </summary>
        public (int line, string instruction) CalculateLine(
            string validationPath, bool print = false, bool functionRelative = false)
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
                var parts = validationPath.Split('.');

                IBlockInstruction pointerInst = null!;
                InstructionSequence seq = null!;
                bool addLocals = false;
                string indent = "";
                foreach (var part in parts)
                {
                    indent += " ";
                    // Skip non-indexed strata (e.g. trailing "NumericInst").
                    if (!part.EndsWith("]"))
                    {
                        line += 1;
                        break;
                    }

                    var match = IndexedTokenRegex.Match(part);
                    if (!match.Success) continue;

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
                                    line += Funcs[i].Size;
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

                            for (int i = 0; i < index; ++i)
                            {
                                var inst = seq[i];
                                if (inst is IBlockInstruction blockInstruction)
                                    line += blockInstruction.BlockSize;
                                else
                                    line += 1;
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
                                pointerInst = blTerm;

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
                                Console.Error.WriteLine(
                                    $"Validation path was invalid. {validationPath}");
                                break;
                            }

                            if (print)
                                Console.WriteLine($"{indent}{strata}[{index}]:{line}");

                            seq = pointerInst.GetBlock(index).Instructions;
                            break;
                        }
                    }
                }

                _pathLineCache[validationPath] = (line, foundInstruction);
            }

            if (!functionRelative)
            {
                line += 1;              // (module
                line += Types.Count;    //   (type
                line += Imports.Length; //   (import
            }
            return (line, foundInstruction);
        }
    }
}
