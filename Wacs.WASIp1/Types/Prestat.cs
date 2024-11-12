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

namespace Wacs.WASIp1.Types
{
    public enum PrestatTag : byte
    {
        Dir = 0,
        NotDir = 1,
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct PrestatDir
    {
        [FieldOffset(0)] public uint NameLen;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Prestat
    {
        [FieldOffset(0)] public PrestatTag Tag;

        [FieldOffset(4)] public PrestatDir Dir;
    }
}