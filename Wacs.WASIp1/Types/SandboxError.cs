using System;
using Wacs.Core.WASIp1;

namespace Wacs.WASIp1.Types
{
    /// <summary>
    /// Represents errors that occur during WASI filesystem sandboxing operations
    /// </summary>
    public class SandboxError : Exception
    {
        /// <summary>
        /// Gets the WASI error number associated with this sandbox violation
        /// </summary>
        public ErrNo ErrorNumber { get; }

        public SandboxError(ErrNo errno, string message)
            : base(message)
        {
            ErrorNumber = errno;
        }

        public SandboxError(ErrNo errno, string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorNumber = errno;
        }
    }
}