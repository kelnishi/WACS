// Copyright 2025 Kelvin Nishikawa
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
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler
{
    public class WatFormatter
    {
        public static void WriteFunction(Module module, FunctionInstance funcInst)
        {
            using StringWriter stringWriter = new StringWriter();
            using IndentedTextWriter writer = new IndentedTextWriter(stringWriter, "    ");
            
            var funcType = (FunctionType)module.Types[funcInst.DefType.DefIndex.Value];
            
            var id = $" (;{funcInst.Index.Value};)";
            var type = $" (type {funcInst.DefType.DefIndex.Value})";
            var param = funcType.ParameterTypes.Arity > 0
                ? funcType.ParameterTypes.ToParameters()
                : "";
            var result = funcType.ResultType.Arity > 0
                ? funcType.ResultType.ToResults()
                : "";
                
            writer.WriteLine($"(func{id}{type}{param}{result}");
            writer.Indent++;
            
            if (funcInst.Locals.Length > 0)
                writer.WriteLine($"(local { string.Join(",",funcInst.Locals.Select(vt => vt.ToNotation()) )})");
            
            foreach (var inst in funcInst.Body.Instructions.Flatten())
            {
                if (inst is InstEnd)
                    writer.Indent--;
                
                if (inst is IBlockInstruction)
                    writer.Write("(");
                
                writer.Write($"{inst.ToNotation()}");
                
                if (inst is InstEnd)
                    writer.Write(")");
                
                writer.WriteLine();
                
                if (inst is IBlockInstruction)
                    writer.Indent++;
            }

            writer.Indent--;
            
            Console.WriteLine(stringWriter.ToString());
        }
    }
}