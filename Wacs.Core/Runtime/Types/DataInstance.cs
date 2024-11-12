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

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.11. Data Instances
    /// </summary>
    public class DataInstance
    {
        public static readonly DataInstance Empty = new(Array.Empty<byte>());

        public readonly byte[] Data;

        public DataInstance(byte[] buf)
        {
            Data = new byte[buf.Length]; // Allocate memory for Data
            Array.Copy(buf, Data, buf.Length); // Copy buf into Data
        }
    }
}