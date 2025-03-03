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

namespace Wacs.Core.Compilation
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class OpLocalAttribute : Attribute
    {
        public int Index;
        public string Type;
        public OpLocalAttribute(int index, string type) 
            => (Index, Type) = (index, type);
    }
    
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class OpParamAttribute : OpLocalAttribute
    {
        public OpParamAttribute(int index, string type) : base(index, type){ } 
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class OpReturnAttribute : Attribute
    {
        public string Type;
        public OpReturnAttribute(string type) 
            => Type = type;
    }
}