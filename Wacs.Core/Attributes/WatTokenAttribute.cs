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

namespace Wacs.Core.Attributes
{
    public class WatTokenAttribute: Attribute
    {
        public readonly string Token;
        public WatTokenAttribute(string token) => Token = token;
    }

    public static class WatTokenExtension
    {
        public static string ToWat<T>(this T v) 
        where T: Enum
        {
            var type = typeof(T);
            var memberInfo = type.GetMember(v.ToString());
            if (memberInfo.Length > 0)
            {
                var attributes = memberInfo[0].GetCustomAttributes(typeof(WatTokenAttribute), false);
                if (attributes.Length > 0)
                {
                    return ((WatTokenAttribute)attributes[0]).Token;
                }
            }
            return v.ToString().ToLower();
        }
    }
}