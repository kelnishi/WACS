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

using Microsoft.Extensions.ObjectPool;
using Wacs.Core.Types;

namespace Wacs.Core.Utilities
{
    public class LocalsSpacePooledObjectPolicy : PooledObjectPolicy<LocalsSpace>
    {
        public override LocalsSpace Create() => new LocalsSpace();

        public override bool Return(LocalsSpace? obj)
        {
            if (obj == null)
                return false;
            obj.Reset();
            return true; 
        }
    }
}