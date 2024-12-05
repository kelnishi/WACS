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

using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.13. External Values
    /// </summary>
    public abstract class ExternalValue
    {
        public abstract ExternalKind Type { get; }

        public class Function : ExternalValue
        {
            public Function(FuncAddr address) => Address = address;
            public override ExternalKind Type => ExternalKind.Function;
            public FuncAddr Address { get; }
        }

        public class Table : ExternalValue
        {
            public Table(TableAddr address) => Address = address;
            public override ExternalKind Type => ExternalKind.Table;
            public TableAddr Address { get; }
        }

        public class Memory : ExternalValue
        {
            public Memory(MemAddr address) => Address = address;
            public override ExternalKind Type => ExternalKind.Memory;
            public MemAddr Address { get; }
        }

        public class Global : ExternalValue
        {
            public Global(GlobalAddr address) => Address = address;
            public override ExternalKind Type => ExternalKind.Global;
            public GlobalAddr Address { get; }
        }
    }
}