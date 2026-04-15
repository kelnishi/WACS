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

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Wacs.Core;
using Wacs.Core.Runtime;

namespace Spec.Test.WastJson
{
    public interface ICommand
    {
        [JsonPropertyName("type")]
        CommandType Type { get; }

        [JsonPropertyName("line")]
        int Line { get; set; }

        public List<Exception> RunTest(WastJson testDefinition, ref WasmRuntime runtime, ref Module? module);
    }
}