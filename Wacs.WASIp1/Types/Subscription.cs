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
using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    [Flags]
    public enum SubclockFlags : ushort
    {
        None = 0,
        SubscriptionClockAbstime = 1,
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct SubscriptionClock
    {
        [FieldOffset(0)] public ClockId Id;

        [FieldOffset(8)] public ulong Timeout;

        [FieldOffset(16)] public ulong Precision;

        [FieldOffset(24)] public SubclockFlags Flags;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct SubscriptionFdReadWrite
    {
        [FieldOffset(0)] public uint Fd;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct SubscriptionUnion
    {
        [FieldOffset(0)] public EventType Tag;
        [FieldOffset(8)] public SubscriptionClock Clock;
        [FieldOffset(8)] public SubscriptionFdReadWrite FdRead;
        [FieldOffset(8)] public SubscriptionFdReadWrite FdWrite;
        [FieldOffset(8)] public SubscriptionFdReadWrite FdReadWrite;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct Subscription
    {
        [FieldOffset(0)]
        public ulong UserData;
    
        [FieldOffset(8)]
        public SubscriptionUnion Union;
    }
}