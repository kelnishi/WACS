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

using Microsoft.Extensions.ObjectPool;

namespace Wacs.Core.Utilities
{
    public class StackPoolPolicy<T> : PooledObjectPolicy<T>
    where T : class, IPoolable, new()
    {
        public override T Create() => new T();

        public override bool Return(T? obj)
        {
            if (obj == null)
                return false;
            
            obj.Clear();
            return true; 
        }
    }
}