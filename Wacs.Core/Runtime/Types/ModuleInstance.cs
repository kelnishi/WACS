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
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.5. Module Instances
    /// Represents an instantiated WebAssembly module, containing the runtime instances of functions, tables, memories, and globals.
    /// </summary>
    public class ModuleInstance
    {
        public ModuleInstance(Module module)
        {
            Types = new TypesSpace(module);
            Repr = module;
        }

        public string Name { get; set; } = "_";

        public Module Repr { get; }

        public TypesSpace Types { get; }
        public FuncAddrs FuncAddrs { get; } = new();

        public FuncAddr StartFunc { get; set; } = FuncAddr.Null;

        public TableAddrs TableAddrs { get; } = new();
        public MemAddrs MemAddrs { get; } = new();
        public GlobalAddrs GlobalAddrs { get; } = new();
        public ElemAddrs ElemAddrs { get; } = new();
        public DataAddrs DataAddrs { get; } = new();
        public List<ExportInstance> Exports { get; } = new();
    }
}