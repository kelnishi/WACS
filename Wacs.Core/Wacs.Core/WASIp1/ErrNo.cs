// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Wacs.Core.Attributes;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.WASIp1
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
        [Signal("No error occurred. System call completed successfully.")]
        Success = 0,

        /// <summary>
        /// Argument list too long.
        /// </summary>
        [Signal("Argument list too long.")]
        TooBig = 1,

        /// <summary>
        /// Permission denied.
        /// </summary>
        [Signal("Permission denied.")]
        Acces = 2,

        /// <summary>
        /// Address in use.
        /// </summary>
        [Signal("Address in use.")]
        AddrInUse = 3,

        /// <summary>
        /// Address not available.
        /// </summary>
        [Signal("Address not available.")]
        AddrNotAvail = 4,

        /// <summary>
        /// Address family not supported.
        /// </summary>
        [Signal("Address family not supported.")]
        AfNoSupport = 5,

        /// <summary>
        /// Resource unavailable, or operation would block.
        /// </summary>
        [Signal("Resource unavailable, or operation would block.")]
        Again = 6,

        /// <summary>
        /// Connection already in progress.
        /// </summary>
        [Signal("Connection already in progress.")]
        Already = 7,

        /// <summary>
        /// Bad file descriptor.
        /// </summary>
        [Signal("Bad file descriptor.")]
        Badf = 8,

        /// <summary>
        /// Bad message.
        /// </summary>
        [Signal("Bad message.")]
        BadMsg = 9,

        /// <summary>
        /// Device or resource busy.
        /// </summary>
        [Signal("Device or resource busy.")]
        Busy = 10,

        /// <summary>
        /// Operation canceled.
        /// </summary>
        [Signal("Operation canceled.")]
        Canceled = 11,

        /// <summary>
        /// No child processes.
        /// </summary>
        [Signal("No child processes.")]
        Child = 12,

        /// <summary>
        /// Connection aborted.
        /// </summary>
        [Signal("Connection aborted.")]
        ConnAborted = 13,

        /// <summary>
        /// Connection refused.
        /// </summary>
        [Signal("Connection refused.")]
        ConnRefused = 14,

        /// <summary>
        /// Connection reset.
        /// </summary>
        [Signal("Connection reset.")]
        ConnReset = 15,

        /// <summary>
        /// Resource deadlock would occur.
        /// </summary>
        [Signal("Resource deadlock would occur.")]
        Deadlk = 16,

        /// <summary>
        /// Destination address required.
        /// </summary>
        [Signal("Destination address required.")]
        DestAddrReq = 17,

        /// <summary>
        /// Mathematics argument out of domain of function.
        /// </summary>
        [Signal("Mathematics argument out of domain of function.")]
        Dom = 18,

        /// <summary>
        /// Reserved.
        /// </summary>
        [Signal("Reserved.")]
        DQuot = 19,

        /// <summary>
        /// File exists.
        /// </summary>
        [Signal("File exists.")]
        Exist = 20,

        /// <summary>
        /// Bad address.
        /// </summary>
        [Signal("Bad address.")]
        Fault = 21,

        /// <summary>
        /// File too large.
        /// </summary>
        [Signal("File too large.")]
        FBig = 22,

        /// <summary>
        /// Host is unreachable.
        /// </summary>
        [Signal("Host is unreachable.")]
        HostUnreach = 23,

        /// <summary>
        /// Identifier removed.
        /// </summary>
        [Signal("Identifier removed.")]
        IDRM = 24,

        /// <summary>
        /// Illegal byte sequence.
        /// </summary>
        [Signal("Illegal byte sequence.")]
        ILSeq = 25,

        /// <summary>
        /// Operation in progress.
        /// </summary>
        [Signal("Operation in progress.")]
        InProgress = 26,

        /// <summary>
        /// Interrupted function.
        /// </summary>
        [Signal("Interrupted function.")]
        Intr = 27,

        /// <summary>
        /// Invalid argument.
        /// </summary>
        [Signal("Invalid argument.")]
        Inval = 28,

        /// <summary>
        /// I/O error.
        /// </summary>
        [Signal("I/O error.")]
        IO = 29,

        /// <summary>
        /// Socket is connected.
        /// </summary>
        [Signal("Socket is connected.")]
        IsConn = 30,

        /// <summary>
        /// Is a directory.
        /// </summary>
        [Signal("Is a directory.")]
        IsDir = 31,

        /// <summary>
        /// Too many levels of symbolic links.
        /// </summary>
        [Signal("Too many levels of symbolic links.")]
        Loop = 32,

        /// <summary>
        /// File descriptor value too large.
        /// </summary>
        [Signal("File descriptor value too large.")]
        MFile = 33,

        /// <summary>
        /// Too many links.
        /// </summary>
        [Signal("Too many links.")]
        MLink = 34,

        /// <summary>
        /// Message too large.
        /// </summary>
        [Signal("Message too large.")]
        MsgSize = 35,

        /// <summary>
        /// Reserved.
        /// </summary>
        [Signal("Reserved.")]
        MultiHop = 36,

        /// <summary>
        /// Filename too long.
        /// </summary>
        [Signal("Filename too long.")]
        NameTooLong = 37,

        /// <summary>
        /// Network is down.
        /// </summary>
        [Signal("Network is down.")]
        NetDown = 38,

        /// <summary>
        /// Connection aborted by network.
        /// </summary>
        [Signal("Connection aborted by network.")]
        NetReset = 39,

        /// <summary>
        /// Network unreachable.
        /// </summary>
        [Signal("Network unreachable.")]
        NetUnreach = 40,

        /// <summary>
        /// Too many files open in system.
        /// </summary>
        [Signal("Too many files open in system.")]
        NFile = 41,

        /// <summary>
        /// No buffer space available.
        /// </summary>
        [Signal("No buffer space available.")]
        NoBufs = 42,

        /// <summary>
        /// No such device.
        /// </summary>
        [Signal("No such device.")]
        NoDev = 43,

        /// <summary>
        /// No such file or directory.
        /// </summary>
        [Signal("No such file or directory.")]
        NoEnt = 44,

        /// <summary>
        /// Executable file format error.
        /// </summary>
        [Signal("Executable file format error.")]
        NoExec = 45,

        /// <summary>
        /// No locks available.
        /// </summary>
        [Signal("No locks available.")]
        NoLck = 46,

        /// <summary>
        /// Reserved.
        /// </summary>
        [Signal("Reserved.")]
        NoLink = 47,

        /// <summary>
        /// Not enough space.
        /// </summary>
        [Signal("Not enough space.")]
        NoMem = 48,

        /// <summary>
        /// No message of the desired type.
        /// </summary>
        [Signal("No message of the desired type.")]
        NoMsg = 49,

        /// <summary>
        /// Protocol not available.
        /// </summary>
        [Signal("Protocol not available.")]
        NoProtoOpt = 50,

        /// <summary>
        /// No space left on device.
        /// </summary>
        [Signal("No space left on device.")]
        NoSpc = 51,

        /// <summary>
        /// Function not supported.
        /// </summary>
        [Signal("Function not supported.")]
        NoSys = 52,

        /// <summary>
        /// The socket is not connected.
        /// </summary>
        [Signal("The socket is not connected.")]
        NotConn = 53,

        /// <summary>
        /// Not a directory or a symbolic link to a directory.
        /// </summary>
        [Signal("Not a directory or a symbolic link to a directory.")]
        NotDir = 54,

        /// <summary>
        /// Directory not empty.
        /// </summary>
        [Signal("Directory not empty.")]
        NotEmpty = 55,

        /// <summary>
        /// State not recoverable.
        /// </summary>
        [Signal("State not recoverable.")]
        NotRecoverable = 56,

        /// <summary>
        /// Not a socket.
        /// </summary>
        [Signal("Not a socket.")]
        NotSock = 57,

        /// <summary>
        /// Not supported, or operation not supported on socket.
        /// </summary>
        [Signal("Not supported, or operation not supported on socket.")]
        NotSup = 58,

        /// <summary>
        /// Inappropriate I/O control operation.
        /// </summary>
        [Signal("Inappropriate I/O control operation.")]
        NotTty = 59,

        /// <summary>
        /// No such device or address.
        /// </summary>
        [Signal("No such device or address.")]
        NxIO = 60,

        /// <summary>
        /// Value too large to be stored in data type.
        /// </summary>
        [Signal("Value too large to be stored in data type.")]
        Overflow = 61,

        /// <summary>
        /// Previous owner died.
        /// </summary>
        [Signal("Previous owner died.")]
        OwnerDead = 62,

        /// <summary>
        /// Operation not permitted.
        /// </summary>
        [Signal("Operation not permitted.")]
        Perm = 63,

        /// <summary>
        /// Broken pipe.
        /// </summary>
        [Signal("Broken pipe.")]
        Pipe = 64,

        /// <summary>
        /// Protocol error.
        /// </summary>
        [Signal("Protocol error.")]
        Proto = 65,

        /// <summary>
        /// Protocol not supported.
        /// </summary>
        [Signal("Protocol not supported.")]
        ProtoNoSupport = 66,

        /// <summary>
        /// Protocol wrong type for socket.
        /// </summary>
        [Signal("Protocol wrong type for socket.")]
        Prototype = 67,

        /// <summary>
        /// Result too large.
        /// </summary>
        [Signal("Result too large.")]
        Range = 68,

        /// <summary>
        /// Read-only file system.
        /// </summary>
        [Signal("Read-only file system.")]
        RoFs = 69,

        /// <summary>
        /// Invalid seek.
        /// </summary>
        [Signal("Invalid seek.")]
        Spipe = 70,

        /// <summary>
        /// No such process.
        /// </summary>
        [Signal("No such process.")]
        Srch = 71,

        /// <summary>
        /// Reserved.
        /// </summary>
        [Signal("Reserved.")]
        Stale = 72,

        /// <summary>
        /// Connection timed out.
        /// </summary>
        [Signal("Connection timed out.")]
        TimedOut = 73,

        /// <summary>
        /// Text file busy.
        /// </summary>
        [Signal("Text file busy.")]
        TxtBsy = 74,

        /// <summary>
        /// Cross-device link.
        /// </summary>
        [Signal("Cross-device link.")]
        XDev = 75,

        /// <summary>
        /// Extension: Capabilities insufficient.
        /// </summary>
        [Signal("Capabilities insufficient.")]
        NotCapable = 76
    }

    public static class ErrNoExtension
    {
        public static string HumanReadable(this ErrNo sig)
        {
            var type = typeof(ErrNo);
            var memberInfo = type.GetMember(sig.ToString());
            if (memberInfo.Length <= 0)
                return $"Signal: {sig}";

            var attributes = memberInfo[0].GetCustomAttributes(typeof(SignalAttribute), false);
            if (attributes.Length > 0)
            {
                return $"{sig}: {((SignalAttribute)attributes[0]).HumanReadableMessage}";
            }

            return $"Signal: {sig}";
        }
    }
}