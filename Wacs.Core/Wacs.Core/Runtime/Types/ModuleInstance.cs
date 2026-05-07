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
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.5. Module Instances
    /// Represents an instantiated WebAssembly module, containing the runtime instances of functions, tables, memories, and globals.
    /// </summary>
    public class ModuleInstance
    {
        public readonly DataAddrs DataAddrs = new();
        public readonly ElemAddrs ElemAddrs = new();
        public readonly ExnAddrs ExnAddrs = new();
        public readonly List<ExportInstance> Exports = new();
        public readonly FuncAddrs FuncAddrs = new();
        public readonly GlobalAddrs GlobalAddrs = new();
        public readonly MemAddrs MemAddrs = new();

        public readonly Module Repr;

        public readonly TableAddrs TableAddrs = new();
        public readonly TagAddrs TagAddrs = new();

        public readonly TypesSpace Types;

        public ModuleInstance(Module module)
        {
            Types = new TypesSpace(module);
            Repr = module;
        }

        public string Name { get; set; } = "_";

        public FuncAddr StartFunc { get; set; } = FuncAddr.Null;

        public void DerefTypes(Span<Value> span)
        {
            for (int i = 0, l = span.Length; i < l; ++i)
            {
                if (span[i].Type.IsDefType())
                {
                    var derefType = Types[span[i].Type.Index()];
                    span[i].Type = derefType.Expansion switch
                    {
                        StructType => ValType.Struct,
                        ArrayType => ValType.Array,
                        FunctionType => ValType.Func,
                        _ => span[i].Type
                    };
                }
            }
        }
    }
}