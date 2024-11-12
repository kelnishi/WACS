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

using System.Runtime.InteropServices;
using dircookie = System.UInt64;
using inode = System.UInt64;
using dirnamlen = System.UInt32;


namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit)]
    public struct DirEnt
    {
        [FieldOffset(0)] public dircookie DNext;
        [FieldOffset(8)] public inode DIno;
        [FieldOffset(16)] public dirnamlen DNamlen;
        [FieldOffset(20)] public Filetype DType;
    }
}