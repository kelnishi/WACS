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
using Wacs.Core.Types;

namespace Wacs.Core.Attributes
{
    /// <summary>
    /// Decorate your host function parameter types for automatic conversion
    /// Enum types will get converted as numerics.
    /// Structs must implement ITypeConvertable to provide marshaling conversion from numerics.
    /// </summary>
    public class WasmTypeAttribute : Attribute
    {
        public WasmTypeAttribute(string typename)
        {
            try
            {
                Type = Enum.Parse<ValType>(typename);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"Invalid ValType: {typename}");
            }
        }

        public ValType Type { get; set; }
    }

    public static class WasmTypeExtension
    {
        public static ValType? GetWasmType(this Type type)
        {
            var attribute = (WasmTypeAttribute)Attribute.GetCustomAttribute(type, typeof(WasmTypeAttribute));
            return attribute?.Type;
        }
    }
}