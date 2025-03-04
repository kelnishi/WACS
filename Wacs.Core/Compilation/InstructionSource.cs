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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Compilation
{
    public partial class InstructionSource
    {
        public ByteCode Op;
        public Dictionary<int, (string type, bool isparam)> Locals = new();
        public string Template;
        public string Return;

        public int ParameterCount => Locals.Values.Select((t, p) => p).Count();

        private static Dictionary<ByteCode, MethodInfo> _sources = new(); 
        
        static InstructionSource()
        {
            var methods = typeof(InstructionSource).GetMethods(BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(OpSourceAttribute), false);
                if (attributes.Length == 0)
                    continue;

                var opsource = attributes[0] as OpSourceAttribute;
                if (opsource is null)
                    continue;

                _sources[opsource.Op] = method;
            }
        }
        
        public static InstructionSource? Get(ByteCode opcode)
        {
            if (!_sources.TryGetValue(opcode, out var mi))
                return null;

            var opParams = mi.GetCustomAttributes(typeof(OpParamAttribute), false) as OpParamAttribute[];
            var opLocals = mi.GetCustomAttributes(typeof(OpLocalAttribute), false) as OpLocalAttribute[];
            var opReturns = mi.GetCustomAttributes(typeof(OpReturnAttribute), false) as OpReturnAttribute[];

            var locals = new Dictionary<int, (string type, bool isparam)>();
            string returns = "void";
            if (opParams != null)
                foreach (var opParam in opParams)
                {
                    locals[opParam.Index] = (opParam.Type, true);
                }

            if (opLocals != null)
                foreach (var opLocal in opLocals)
                {
                    locals[opLocal.Index] = (opLocal.Type, true);
                }

            if (opReturns != null && opReturns.Length == 1)
                returns = opReturns[0].Type;

            string template = mi.Invoke(null, null) as string;
            var src = new InstructionSource
            {
                Op = opcode,
                Locals = locals,
                Return = returns,
                Template = template
            };
            
            return src;
        }

        public override string ToString() => $"InstructionSource({Op})";
    }
}