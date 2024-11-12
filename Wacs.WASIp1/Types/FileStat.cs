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
using device = System.UInt64;
using inode = System.UInt64;
using linkcount = System.UInt64;
using filesize = System.UInt64;
using timestamp = System.UInt64;

namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct FileStat
    {
        [FieldOffset(0)] public device Device;
        [FieldOffset(8)] public inode Ino;
        [FieldOffset(16)] public Filetype Mode;
        [FieldOffset(24)] public linkcount NLink;
        [FieldOffset(32)] public filesize Size;
        [FieldOffset(40)] public timestamp ATim;
        [FieldOffset(48)] public timestamp MTim;
        [FieldOffset(56)] public timestamp CTim;
    }
}