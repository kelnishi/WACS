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
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct FdStat
    {
        [FieldOffset(0)] public Filetype Filetype;
        [FieldOffset(2)] public FdFlags Flags;
        [FieldOffset(8)] public Rights RightsBase;
        [FieldOffset(16)] public Rights RightsInheriting;
    }
}