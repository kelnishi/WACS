using System;
using System.Linq;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.WASIp1.Types;
using ptr = System.Int32;
using size = System.UInt32;

namespace Wacs.WASIp1
{
    /// <summary>
    /// Implements WASI environment variable and argument functions.
    /// </summary>
    public class Env : IBindable
    {
        private readonly WasiConfiguration _config;

        public Env(WasiConfiguration config)
        {
            _config = config;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "args_sizes_get"), ArgsSizesGet);
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "args_get"), ArgsGet);
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "environ_sizes_get"), EnvironSizesGet);
            runtime.BindHostFunction<Func<ExecContext,ptr,ptr,ErrNo>>((module, "environ_get"), EnvironGet);
        }

        /// <summary>
        /// Copies the number and size of the command-line arguments to linear memory.
        /// </summary>
        public ErrNo ArgsSizesGet(ExecContext ctx, ptr argcPtr, ptr argvBufSizePtr)
        {
            var mem = ctx.DefaultMemory;
            
            int argc = _config.Arguments.Count;
            int argvBufSize = _config.Arguments.Sum(arg => Encoding.UTF8.GetByteCount(arg) + 1);

            // Write the counts to the provided pointers.
            mem.WriteInt32(argcPtr, argc);
            mem.WriteInt32(argvBufSizePtr, argvBufSize);
            
            return ErrNo.Success;
        }

        /// <summary>
        /// Copies command-line argument data to linear memory.
        /// </summary>
        public ErrNo ArgsGet(ExecContext ctx, ptr argvPtr, ptr argvBufPtr)
        {
            var mem = ctx.DefaultMemory;
            
            foreach (string arg in _config.Arguments)
            {
                // Copy argument string to argvBufPtr.
                int strLen = mem.WriteUtf8String((uint)argvBufPtr, arg, true);
                
                // Write pointer to argument in argvPtr.
                mem.WriteInt32(argvPtr, argvBufPtr);

                // Update offsets.
                argvBufPtr += strLen;
                argvPtr += sizeof(ptr);
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Copies the number and size of the environment variables to linear memory.
        /// </summary>
        public ErrNo EnvironSizesGet(ExecContext ctx, ptr environCountPtr, ptr environBufSizePtr)
        {
            var mem = ctx.DefaultMemory;
            // Get the total number of environment variables.
            size environCount = (size)_config.EnvironmentVariables.Count;
            size environBufSize = (size)_config.EnvironmentVariables.Sum(envVar =>
                Encoding.UTF8.GetByteCount($"{envVar.Key}={envVar.Value}") + 1);
            // Write the counts to the provided pointers.
            mem.WriteInt32(environCountPtr, environCount);
            mem.WriteInt32(environBufSizePtr,environBufSize);
            return ErrNo.Success;
        }

        /// <summary>
        /// Copies environment variable data to linear memory.
        /// </summary>
        public ErrNo EnvironGet(ExecContext ctx, ptr environPtr, ptr environBufPtr)
        {
            var mem = ctx.DefaultMemory;
            foreach (var envVar in _config.EnvironmentVariables)
            {
                string envEntry = $"{envVar.Key}={envVar.Value}";

                // Copy environment string to environBufPtr.
                int strLen = mem.WriteUtf8String((uint)environBufPtr, envEntry, true);

                // Write pointer to environment variable in environPtr.
                mem.WriteInt32(environPtr, environBufPtr);
                
                // Update offsets.
                environBufPtr += strLen;
                environPtr += sizeof(ptr);
            }

            return ErrNo.Success;
        }
    }
}