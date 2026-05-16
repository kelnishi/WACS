// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Silk.NET.WebGPU;
using Wacs.ComponentModel.Runtime;
using GenWebgpu = Wacs.WASI.GFX.Webgpu.Webgpu;

namespace Wacs.WASI.GFX.Silk
{
    /// <summary>
    /// Silk-backed wrapper around a wgpu <c>ShaderModule*</c>.
    /// Created from a WGSL source string via the wgpu
    /// ShaderModuleWGSLDescriptor chained struct.
    /// </summary>
    internal sealed unsafe class SilkGpuShaderModule
        : GenWebgpu.IGpuShaderModule, IDisposable
    {
        private readonly SilkGpuBackend _backend;
        private readonly Device* _device;
        private ShaderModule* _module;
        private string _label;
        private bool _disposed;

        public SilkGpuShaderModule(SilkGpuBackend backend, Device* device,
            ShaderModule* module, string label)
        {
            _backend = backend;
            _device = device;
            _module = module;
            _label = label ?? string.Empty;
        }

        internal ShaderModule* Native => _module;

        public string Label() => _label;
        public void SetLabel(string label)
        {
            EnsureLive();
            _label = label ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(_label + "\0");
            fixed (byte* p = bytes)
            {
                _backend.EnsureApi().ShaderModuleSetLabel(_module, p);
            }
        }

        public GenWebgpu.IGpuCompilationInfo GetCompilationInfo()
        {
            EnsureLive();
            bool done = false;
            GenWebgpu.IGpuCompilationMessage[] messages =
                System.Array.Empty<GenWebgpu.IGpuCompilationMessage>();
            var cb = new PfnCompilationInfoCallback(
                (CompilationInfoRequestStatus status,
                    CompilationInfo* info, void* _) =>
                {
                    if (status == CompilationInfoRequestStatus.Success
                        && info != null && info->Messages != null)
                    {
                        var n = (int)info->MessageCount;
                        var arr = new GenWebgpu.IGpuCompilationMessage[n];
                        for (int i = 0; i < n; i++)
                        {
                            var m = info->Messages[i];
                            var msgText = SilkGpuBackend.DecodeUtf8(m.Message);
                            arr[i] = new SilkGpuCompilationMessage(
                                msgText,
                                MapCompilationMessageType(m.Type),
                                m.LineNum, m.LinePos, m.Offset, m.Length);
                        }
                        messages = arr;
                    }
                    done = true;
                });
            _backend.EnsureApi().ShaderModuleGetCompilationInfo(
                _module, cb, null);
            _backend.PollUntilDone(_device, () => done);
            return new SilkGpuCompilationInfo(messages);
        }

        private static GenWebgpu.GpuCompilationMessageType
            MapCompilationMessageType(CompilationMessageType t)
            => t switch
            {
                CompilationMessageType.Error => GenWebgpu.GpuCompilationMessageType.Error,
                CompilationMessageType.Warning => GenWebgpu.GpuCompilationMessageType.Warning,
                CompilationMessageType.Info => GenWebgpu.GpuCompilationMessageType.Info,
                _ => GenWebgpu.GpuCompilationMessageType.Info,
            };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_module != null)
            {
                _backend.EnsureApi().ShaderModuleRelease(_module);
                _module = null;
            }
        }

        private void EnsureLive()
        {
            if (_disposed || _module == null)
                throw new ObjectDisposedException(nameof(SilkGpuShaderModule));
        }
    }

    /// <summary>Carrier for the GetCompilationInfo result.
    /// Holds the message array captured from wgpu inside the
    /// poll-bridged callback.</summary>
    internal sealed class SilkGpuCompilationInfo
        : GenWebgpu.IGpuCompilationInfo
    {
        private readonly GenWebgpu.IGpuCompilationMessage[] _messages;
        public SilkGpuCompilationInfo(GenWebgpu.IGpuCompilationMessage[] messages)
        {
            _messages = messages
                ?? System.Array.Empty<GenWebgpu.IGpuCompilationMessage>();
        }
        public GenWebgpu.IGpuCompilationMessage[] Messages() => _messages;
    }

    /// <summary>Captured wgpu compilation message. Owned by the
    /// WIT resource table once allocated; no wgpu handle to
    /// release (wgpu's pointer is callback-scope only).</summary>
    internal sealed class SilkGpuCompilationMessage
        : GenWebgpu.IGpuCompilationMessage
    {
        private readonly string _message;
        private readonly GenWebgpu.GpuCompilationMessageType _type;
        private readonly ulong _lineNum, _linePos, _offset, _length;
        public SilkGpuCompilationMessage(string message,
            GenWebgpu.GpuCompilationMessageType type,
            ulong lineNum, ulong linePos, ulong offset, ulong length)
        {
            _message = message ?? string.Empty;
            _type = type;
            _lineNum = lineNum;
            _linePos = linePos;
            _offset = offset;
            _length = length;
        }
        public string Message() => _message;
        public GenWebgpu.GpuCompilationMessageType Type() => _type;
        public ulong LineNum() => _lineNum;
        public ulong LinePos() => _linePos;
        public ulong Offset() => _offset;
        public ulong Length() => _length;
    }
}
