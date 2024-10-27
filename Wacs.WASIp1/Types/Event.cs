using System.Runtime.InteropServices;

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