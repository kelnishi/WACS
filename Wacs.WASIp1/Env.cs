using System;
using System.Linq;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    /// <summary>
    /// Implements WASI environment variable and argument functions.
    /// </summary>
    public class Env : IBindable
    {
        private readonly State _state;

        public Env(State state)
        {
            _state = state;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,int,int,ErrNo>>((module, "args_sizes_get"), ArgsSizesGet);
            runtime.BindHostFunction<Func<ExecContext,int,int,ErrNo>>((module, "args_get"), ArgsGet);
            runtime.BindHostFunction<Func<ExecContext,int,int,ErrNo>>((module, "environ_sizes_get"), EnvironSizesGet);
            runtime.BindHostFunction<Func<ExecContext,int,int,ErrNo>>((module, "environ_get"), EnvironGet);
        }

        /// <summary>
        /// Copies the number and size of the command-line arguments to linear memory.
        /// </summary>
        public ErrNo ArgsSizesGet(ExecContext ctx, int argcPtr, int argvBufSizePtr)
        {
            var mem = ctx.DefaultMemory;
            
            var argcMem = mem[argcPtr..(argcPtr + 4)];
            var argvMem = mem[argvBufSizePtr..(argvBufSizePtr + 4)];
            
            int argc = _state.Arguments.Count;
            int argvBufSize = _state.Arguments.Sum(arg => Encoding.UTF8.GetByteCount(arg) + 1);

            // Write the counts to the provided pointers.
            argcMem.WriteInt32(argc);
            argvMem.WriteInt32(argvBufSize);

            return ErrNo.Success;
        }

        /// <summary>
        /// Copies command-line argument data to linear memory.
        /// </summary>
        public ErrNo ArgsGet(ExecContext ctx, int argvPtr, int argvBufPtr)
        {
            var mem = ctx.DefaultMemory;
            
            int offset = 0;
            foreach (string arg in _state.Arguments)
            {
                string argNullTerminated = arg + '\0';
                byte[] argBytes = Encoding.UTF8.GetBytes(argNullTerminated);

                // Copy argument string to argvBufPtr.
                var argvBufMem = mem[argvBufPtr..(argvBufPtr+argBytes.Length)];
                argBytes.CopyTo(argvBufMem);

                // Write pointer to argument in argvPtr.
                var argvMem = mem[argvPtr..(argvPtr+4)];
                argvMem.WriteInt32(argvBufPtr+offset);
                // Marshal.WriteIntPtr(argvPtr, argvBufPtr + offset);

                // Update offsets.
                offset += argBytes.Length;
                argvPtr += IntPtr.Size;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Copies the number and size of the environment variables to linear memory.
        /// </summary>
        public ErrNo EnvironSizesGet(ExecContext ctx, int environCountPtr, int environBufSizePtr)
        {
            var mem = ctx.DefaultMemory;
            
            // Get the total number of environment variables.
            var environCountMem = mem[environCountPtr..(environCountPtr + 4)];
            var environBufSizeMem = mem[environBufSizePtr..(environBufSizePtr + 4)];
    
            int environCount = _state.EnvironmentVariables.Count;
            int environBufSize = _state.EnvironmentVariables.Sum(envVar =>
                Encoding.UTF8.GetByteCount($"{envVar.Key}={envVar.Value}") + 1);
            // Write the counts to the provided pointers.
            environCountMem.WriteInt32(environCount);
            environBufSizeMem.WriteInt32(environBufSize);
            return ErrNo.Success;
        }

        /// <summary>
        /// Copies environment variable data to linear memory.
        /// </summary>
        public ErrNo EnvironGet(ExecContext ctx, int environPtr, int environBufPtr)
        {
            var mem = ctx.DefaultMemory;
            int offset = 0;
            foreach (var envVar in _state.EnvironmentVariables)
            {
                string envEntry = $"{envVar.Key}={envVar.Value}\0";
                byte[] envBytes = Encoding.UTF8.GetBytes(envEntry);

                // Copy environment string to environBufPtr.
                var environBufMem = mem[environBufPtr..(environBufPtr + envBytes.Length)];
                envBytes.CopyTo(environBufMem);

                // Write pointer to environment variable in environPtr.
                var environMem = mem[environPtr..(environPtr + 4)];
                environMem.WriteInt32(environBufPtr + offset);

                // Update offsets.
                offset += envBytes.Length;
                environPtr += IntPtr.Size;
            }

            return ErrNo.Success;
        }
    }
}