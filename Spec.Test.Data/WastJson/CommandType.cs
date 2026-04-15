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

using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Spec.Test.WastJson
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CommandType
    {
        [EnumMember(Value = "module")]
        Module,
        
        [EnumMember(Value = "module_definition")]
        ModuleDefinition,
        
        [EnumMember(Value = "register")]
        Register,

        [EnumMember(Value = "action")]
        Action,
        
        [EnumMember(Value = "assert_return")]
        AssertReturn,

        [EnumMember(Value = "assert_trap")]
        AssertTrap,
        
        [EnumMember(Value = "assert_exception")]
        AssertException,

        [EnumMember(Value = "assert_exhaustion")]
        AssertExhaustion,

        [EnumMember(Value = "assert_invalid")]
        AssertInvalid,

        [EnumMember(Value = "assert_malformed")]
        AssertMalformed,

        [EnumMember(Value = "assert_unlinkable")]
        AssertUnlinkable,

        [EnumMember(Value = "assert_uninstantiable")]
        AssertUninstantiable,

        [EnumMember(Value = "invoke")]
        Invoke,

        [EnumMember(Value = "get")]
        Get,

        [EnumMember(Value = "set")]
        Set,

        [EnumMember(Value = "start")]
        Start,

        [EnumMember(Value = "assert_return_canonical_nans")]
        AssertReturnCanonicalNans,

        [EnumMember(Value = "assert_return_arithmetic_nans")]
        AssertReturnArithmeticNans,

        [EnumMember(Value = "assert_return_detached")]
        AssertReturnDetached,

        [EnumMember(Value = "assert_terminated")]
        AssertTerminated,

        [EnumMember(Value = "assert_undefined")]
        AssertUndefined,

        [EnumMember(Value = "assert_exclude_from_must")]
        AssertExcludeFromMust,

        [EnumMember(Value = "module_instance")]
        ModuleInstance,

        [EnumMember(Value = "module_exclusive")]
        ModuleExclusive,

        [EnumMember(Value = "pump")]
        Pump,

        [EnumMember(Value = "maybe")]
        Maybe,
        
        [EnumMember(Value = "break")]
        DebuggerBreak,
    }
}