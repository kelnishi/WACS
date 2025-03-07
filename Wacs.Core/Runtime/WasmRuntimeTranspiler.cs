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

using Wacs.Core.Runtime.Transpiler;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    public partial class WasmRuntime
    {
        public bool TranspileModules = false;

        public void TranspileModule(ModuleInstance moduleInstance)
        {
            foreach (var funcAddr in moduleInstance.FuncAddrs)
            {
                var instance = Store[funcAddr];
                if (instance is FunctionInstance { Definition: { IsImport: false } } functionInstance)
                    if (functionInstance.Module == moduleInstance)
                        FunctionTranspiler.TranspileFunction(functionInstance);
            }
        }
    }
}