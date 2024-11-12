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
using Wacs.Core.WASIp1;

namespace Wacs.WASIp1.Types
{
    public enum EventRwFlags : ushort
    {
        None = 0,
        FdReadwriteHangup = 1,
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct EventFdReadWrite
    {
        [FieldOffset(0)] public ulong NBytes;
        [FieldOffset(8)] public EventRwFlags Flags;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct Event
    {
        [FieldOffset(0)]  public ulong UserData;
        [FieldOffset(8)]  public ErrNo Error;
        [FieldOffset(10)] public EventType Type;
        [FieldOffset(16)] public EventFdReadWrite FdReadWrite;
    }
}