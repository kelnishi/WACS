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
using System.Runtime.Serialization;

namespace Spec.Test.WastJson
{

    public static class EnumHelper
    {
        public static TEnum GetEnumValueFromString<TEnum>(string? value) where TEnum : Enum
        {
            foreach (var field in typeof(TEnum).GetFields())
            {
                if (Attribute.GetCustomAttribute(field, typeof(EnumMemberAttribute)) is EnumMemberAttribute attribute)
                {
                    if (attribute.Value == value)
                        return (TEnum)field.GetValue(null)!;
                }
                else
                {
                    if (field.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return (TEnum)field.GetValue(null)!;
                }
            }
            throw new ArgumentException($"Unknown value '{value}' for enum {typeof(TEnum).Name}");
        }
    }
}