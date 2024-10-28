using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    /// <summary>
    /// WASI error codes as per the WASI specification.
    /// Represents standard POSIX-style error numbers.
    /// https://github.com/WebAssembly/WASI/blob/main/legacy/preview1/docs.md#variant-cases-1
    /// </summary>
    [WasmType(nameof(ValType.I32))]
    public enum ErrNo : ushort
    {
        /// <summary>
        /// No error occurred. System call completed successfully.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Argument list too long.
        /// </summary>
        TooBig = 1,

        /// <summary>
        /// Permission denied.
        /// </summary>
        Acces = 2,

        /// <summary>
        /// Address in use.
        /// </summary>
        AddrInUse = 3,

        /// <summary>
        /// Address not available.
        /// </summary>
        AddrNotAvail = 4,

        /// <summary>
        /// Address family not supported.
        /// </summary>
        AfNoSupport = 5,

        /// <summary>
        /// Resource unavailable, or operation would block.
        /// </summary>
        Again = 6,

        /// <summary>
        /// Connection already in progress.
        /// </summary>
        Already = 7,

        /// <summary>
        /// Bad file descriptor.
        /// </summary>
        Badf = 8,

        /// <summary>
        /// Bad message.
        /// </summary>
        BadMsg = 9,

        /// <summary>
        /// Device or resource busy.
        /// </summary>
        Busy = 10,

        /// <summary>
        /// Operation canceled.
        /// </summary>
        Canceled = 11,

        /// <summary>
        /// No child processes.
        /// </summary>
        Child = 12,

        /// <summary>
        /// Connection aborted.
        /// </summary>
        ConnAborted = 13,

        /// <summary>
        /// Connection refused.
        /// </summary>
        ConnRefused = 14,

        /// <summary>
        /// Connection reset.
        /// </summary>
        ConnReset = 15,

        /// <summary>
        /// Resource deadlock would occur.
        /// </summary>
        Deadlk = 16,

        /// <summary>
        /// Destination address required.
        /// </summary>
        DestAddrReq = 17,

        /// <summary>
        /// Mathematics argument out of domain of function.
        /// </summary>
        Dom = 18,

        /// <summary>
        /// Reserved.
        /// </summary>
        DQuot = 19,

        /// <summary>
        /// File exists.
        /// </summary>
        Exist = 20,

        /// <summary>
        /// Bad address.
        /// </summary>
        Fault = 21,

        /// <summary>
        /// File too large.
        /// </summary>
        FBig = 22,

        /// <summary>
        /// Host is unreachable.
        /// </summary>
        HostUnreach = 23,

        /// <summary>
        /// Identifier removed.
        /// </summary>
        IDRM = 24,

        /// <summary>
        /// Illegal byte sequence.
        /// </summary>
        ILSeq = 25,

        /// <summary>
        /// Operation in progress.
        /// </summary>
        InProgress = 26,

        /// <summary>
        /// Interrupted function.
        /// </summary>
        Intr = 27,

        /// <summary>
        /// Invalid argument.
        /// </summary>
        Inval = 28,

        /// <summary>
        /// I/O error.
        /// </summary>
        IO = 29,

        /// <summary>
        /// Socket is connected.
        /// </summary>
        IsConn = 30,

        /// <summary>
        /// Is a directory.
        /// </summary>
        IsDir = 31,

        /// <summary>
        /// Too many levels of symbolic links.
        /// </summary>
        Loop = 32,

        /// <summary>
        /// File descriptor value too large.
        /// </summary>
        MFile = 33,

        /// <summary>
        /// Too many links.
        /// </summary>
        MLink = 34,

        /// <summary>
        /// Message too large.
        /// </summary>
        MsgSize = 35,

        /// <summary>
        /// Reserved.
        /// </summary>
        MultiHop = 36,

        /// <summary>
        /// Filename too long.
        /// </summary>
        NameTooLong = 37,

        /// <summary>
        /// Network is down.
        /// </summary>
        NetDown = 38,

        /// <summary>
        /// Connection aborted by network.
        /// </summary>
        NetReset = 39,

        /// <summary>
        /// Network unreachable.
        /// </summary>
        NetUnreach = 40,

        /// <summary>
        /// Too many files open in system.
        /// </summary>
        NFile = 41,

        /// <summary>
        /// No buffer space available.
        /// </summary>
        NoBufs = 42,

        /// <summary>
        /// No such device.
        /// </summary>
        NoDev = 43,

        /// <summary>
        /// No such file or directory.
        /// </summary>
        NoEnt = 44,

        /// <summary>
        /// Executable file format error.
        /// </summary>
        NoExec = 45,

        /// <summary>
        /// No locks available.
        /// </summary>
        NoLck = 46,

        /// <summary>
        /// Reserved.
        /// </summary>
        NoLink = 47,

        /// <summary>
        /// Not enough space.
        /// </summary>
        NoMem = 48,

        /// <summary>
        /// No message of the desired type.
        /// </summary>
        NoMsg = 49,

        /// <summary>
        /// Protocol not available.
        /// </summary>
        NoProtoOpt = 50,

        /// <summary>
        /// No space left on device.
        /// </summary>
        NoSpc = 51,

        /// <summary>
        /// Function not supported.
        /// </summary>
        NoSys = 52,

        /// <summary>
        /// The socket is not connected.
        /// </summary>
        NotConn = 53,

        /// <summary>
        /// Not a directory or a symbolic link to a directory.
        /// </summary>
        NotDir = 54,

        /// <summary>
        /// Directory not empty.
        /// </summary>
        NotEmpty = 55,

        /// <summary>
        /// State not recoverable.
        /// </summary>
        NotRecoverable = 56,

        /// <summary>
        /// Not a socket.
        /// </summary>
        NotSock = 57,

        /// <summary>
        /// Not supported, or operation not supported on socket.
        /// </summary>
        NotSup = 58,

        /// <summary>
        /// Inappropriate I/O control operation.
        /// </summary>
        NotTty = 59,

        /// <summary>
        /// No such device or address.
        /// </summary>
        NxIO = 60,

        /// <summary>
        /// Value too large to be stored in data type.
        /// </summary>
        Overflow = 61,

        /// <summary>
        /// Previous owner died.
        /// </summary>
        OwnerDead = 62,

        /// <summary>
        /// Operation not permitted.
        /// </summary>
        Perm = 63,

        /// <summary>
        /// Broken pipe.
        /// </summary>
        Pipe = 64,

        /// <summary>
        /// Protocol error.
        /// </summary>
        Proto = 65,

        /// <summary>
        /// Protocol not supported.
        /// </summary>
        ProtoNoSupport = 66,

        /// <summary>
        /// Protocol wrong type for socket.
        /// </summary>
        Prototype = 67,

        /// <summary>
        /// Result too large.
        /// </summary>
        Range = 68,

        /// <summary>
        /// Read-only file system.
        /// </summary>
        RoFs = 69,

        /// <summary>
        /// Invalid seek.
        /// </summary>
        Spipe = 70,

        /// <summary>
        /// No such process.
        /// </summary>
        Srch = 71,

        /// <summary>
        /// Reserved.
        /// </summary>
        Stale = 72,

        /// <summary>
        /// Connection timed out.
        /// </summary>
        TimedOut = 73,

        /// <summary>
        /// Text file busy.
        /// </summary>
        TxtBsy = 74,

        /// <summary>
        /// Cross-device link.
        /// </summary>
        XDev = 75,

        /// <summary>
        /// Extension: Capabilities insufficient.
        /// </summary>
        NotCapable = 76
    }
}