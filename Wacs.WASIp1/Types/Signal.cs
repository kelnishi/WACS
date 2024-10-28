using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    /// <summary>
    /// WASI signal codes as per the WASI specification.
    /// Represents standard POSIX-style signals.
    /// https://github.com/WebAssembly/WASI/blob/main/legacy/preview1/docs.md#variant-cases-7
    /// </summary>
    [WasmType(nameof(ValType.I32))]
    public enum Signal : byte
    {
        /// <summary>
        /// No signal. Used to indicate the absence of a signal.
        /// </summary>
        None = 0,

        /// <summary>
        /// Hangup detected on controlling terminal or death of controlling process.
        /// </summary>
        SIGHUP = 1,

        /// <summary>
        /// Interrupt from keyboard (Ctrl+C).
        /// </summary>
        SIGINT = 2,

        /// <summary>
        /// Quit from keyboard (Ctrl+\).
        /// </summary>
        SIGQUIT = 3,

        /// <summary>
        /// Illegal instruction.
        /// </summary>
        SIGILL = 4,

        /// <summary>
        /// Trace/breakpoint trap.
        /// </summary>
        SIGTRAP = 5,

        /// <summary>
        /// Abort signal from abort(3).
        /// </summary>
        SIGABRT = 6,

        /// <summary>
        /// Bus error (bad memory access).
        /// </summary>
        SIGBUS = 7,

        /// <summary>
        /// Floating-point exception.
        /// </summary>
        SIGFPE = 8,

        /// <summary>
        /// Kill signal.
        /// </summary>
        SIGKILL = 9,

        /// <summary>
        /// User-defined signal 1.
        /// </summary>
        SIGUSR1 = 10,

        /// <summary>
        /// Invalid memory reference.
        /// </summary>
        SIGSEGV = 11,

        /// <summary>
        /// User-defined signal 2.
        /// </summary>
        SIGUSR2 = 12,

        /// <summary>
        /// Broken pipe: write to pipe with no readers.
        /// </summary>
        SIGPIPE = 13,

        /// <summary>
        /// Timer signal from alarm(2).
        /// </summary>
        SIGALRM = 14,

        /// <summary>
        /// Termination signal.
        /// </summary>
        SIGTERM = 15,

        /// <summary>
        /// Stack fault on coprocessor.
        /// </summary>
        SIGSTKFLT = 16,

        /// <summary>
        /// Child stopped or terminated.
        /// </summary>
        SIGCHLD = 17,

        /// <summary>
        /// Continue if stopped.
        /// </summary>
        SIGCONT = 18,

        /// <summary>
        /// Stop process.
        /// </summary>
        SIGSTOP = 19,

        /// <summary>
        /// Stop typed at terminal.
        /// </summary>
        SIGTSTP = 20,

        /// <summary>
        /// Terminal input for background process.
        /// </summary>
        SIGTTIN = 21,

        /// <summary>
        /// Terminal output for background process.
        /// </summary>
        SIGTTOU = 22,

        /// <summary>
        /// Urgent condition on socket.
        /// </summary>
        SIGURG = 23,

        /// <summary>
        /// CPU time limit exceeded.
        /// </summary>
        SIGXCPU = 24,

        /// <summary>
        /// File size limit exceeded.
        /// </summary>
        SIGXFSZ = 25,

        /// <summary>
        /// Virtual alarm clock.
        /// </summary>
        SIGVTALRM = 26,

        /// <summary>
        /// Profiling timer expired.
        /// </summary>
        SIGPROF = 27,

        /// <summary>
        /// Window resize signal.
        /// </summary>
        SIGWINCH = 28,

        /// <summary>
        /// I/O now possible.
        /// </summary>
        SIGPOLL = 29,

        /// <summary>
        /// Power failure.
        /// </summary>
        SIGPWR = 30,

        /// <summary>
        /// Bad system call.
        /// </summary>
        SIGSYS = 31,
    }
}