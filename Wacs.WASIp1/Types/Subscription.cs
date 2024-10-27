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
        [FieldOffset(0)] public SubscriptionClock Clock;
        [FieldOffset(0)] public SubscriptionFdReadWrite FdRead;
        [FieldOffset(0)] public SubscriptionFdReadWrite FdWrite;
        [FieldOffset(0)] public SubscriptionFdReadWrite FdReadWrite;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct Subscription
    {
        [FieldOffset(0)]
        public ulong UserData;
    
        [FieldOffset(8)]
        public EventType Type;
    
        [FieldOffset(16)]
        public SubscriptionUnion U;
    }
}