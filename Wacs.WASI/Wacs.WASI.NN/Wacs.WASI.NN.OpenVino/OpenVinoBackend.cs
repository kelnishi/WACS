// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using OpenVinoSharp;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;
using OvTensor = OpenVinoSharp.Tensor;
using OvShape = OpenVinoSharp.Shape;
// `OpenVinoSharp.Core` collides with the `Wacs.Core` namespace
// during name resolution; alias to dodge.
using OvCore = OpenVinoSharp.Core;
// `OpenVinoSharp.element.Type` is the element-type wrapper —
// `element` is a child namespace of OpenVinoSharp. Alias to a
// crisp single token.
using OvElementType = OpenVinoSharp.element.Type;

namespace Wacs.WASI.NN.OpenVino
{
    /// <summary>
    /// <see cref="IBackend"/> implementation for
    /// <see cref="GraphEncoding.OpenVINO"/> backed by the
    /// OpenVINO.CSharp.API NuGet (Intel's C# wrapper around
    /// libopenvino). Ships as a sibling package so consumers
    /// wiring only one backend don't pull the OpenVINO native
    /// runtimes (~40 MB per RID across the supported platforms).
    ///
    /// <para>OpenVINO is the one wasi-nn backend the spec
    /// explicitly designed the <see cref="IBackend.LoadGraph"/>
    /// multi-builder shape around: the wire form is
    /// <c>builders = [IR-XML, IR-bin]</c>. The backend reads
    /// <c>builders[0]</c> as the OpenVINO IR XML and
    /// <c>builders[1]</c> as the weights binary; a single-builder
    /// call falls back to the "no weights" case (rare — only
    /// applies to IR models with all constants inlined).</para>
    ///
    /// <para>Lifetime: each <see cref="LoadGraph"/> call invokes
    /// <c>Core.read_model</c> + <c>compile_model</c> to produce
    /// a <see cref="CompiledModel"/> which is held by the
    /// returned <see cref="OpenVinoGraph"/>. The compiled model
    /// is reusable across contexts —
    /// <see cref="CompiledModel.create_infer_request"/> mints a
    /// fresh <see cref="InferRequest"/> per context, which is
    /// the unit of state for OpenVINO inference.</para>
    ///
    /// <para>Device pinning goes through
    /// <see cref="OpenVinoBackendOptions.Device"/> or the
    /// <c>WACS_WASINN_OPENVINO_DEVICE</c> environment variable.
    /// The wasi-nn guest's <see cref="ExecutionTarget"/> request
    /// is a coarser shape — CPU / GPU / TPU — so it overrides
    /// the typed options only when the typed options are at
    /// their default (<see cref="OpenVinoDevice.Auto"/>); a host
    /// that pinned to a specific device through options keeps
    /// that pin regardless of what the guest asks for.</para>
    /// </summary>
    public sealed class OpenVinoBackend : IBackend
    {
        private readonly OpenVinoBackendOptions _options;

        /// <summary>
        /// Create the backend reading
        /// <see cref="OpenVinoBackendOptions.FromEnvironment"/>
        /// for device selection. Default is AUTO unless
        /// <c>WACS_WASINN_OPENVINO_DEVICE</c> is set.
        /// </summary>
        public OpenVinoBackend() : this(OpenVinoBackendOptions.FromEnvironment()) { }

        /// <summary>
        /// Create the backend with typed
        /// <see cref="OpenVinoBackendOptions"/>. Useful for
        /// library embedders that want to pin a device or set
        /// OpenVINO runtime properties at startup without
        /// touching env vars.
        /// </summary>
        public OpenVinoBackend(OpenVinoBackendOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }
            = new[] { GraphEncoding.OpenVINO };

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            if (builders.Count == 0)
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    "graph.load received an empty builder list; "
                    + "OpenVINO needs at least an IR XML buffer");

            // The wasi-nn convention for OpenVINO is two builders:
            // [0] = IR XML, [1] = weights .bin. Single-builder
            // calls are allowed (some IRs inline all constants)
            // and route to read_model with empty weights.
            byte[] xmlBytes = builders[0].ToArray();
            byte[] weightsBytes = builders.Count > 1
                ? builders[1].ToArray()
                : Array.Empty<byte>();

            string deviceName = ChooseDeviceName(target);

            try
            {
                // Build a u8 Tensor wrapping the weights bytes —
                // OpenVINO's read_model(byte[], Tensor) overload
                // takes XML as bytes but weights as a Tensor so
                // the runtime can map them directly into the
                // model graph without an extra copy at compile
                // time.
                var weightsTensor = new OvTensor(
                    new OvElementType(ElementType.U8),
                    new OvShape(new long[] { weightsBytes.Length }),
                    weightsBytes.Length == 0 ? new byte[0] : weightsBytes);

                var core = new OvCore();
                var model = core.read_model(xmlBytes, weightsTensor);
                CompiledModel compiled;
                try
                {
                    compiled = core.compile_model(
                        model, deviceName, _options.Properties);
                }
                catch (Exception) when (_options.FallbackToCpu
                    && !string.Equals(deviceName, "CPU", StringComparison.Ordinal))
                {
                    // Plugin missing / device init failed — retry
                    // on CPU. The CPU plugin ships in every
                    // OpenVINO runtime distribution, so this
                    // path is the safest landing zone.
                    compiled = core.compile_model(
                        model, "CPU", _options.Properties);
                }

                // Tensors created from the weights bytes can be
                // disposed once compile_model has consumed them.
                weightsTensor.Dispose();
                return new OpenVinoGraph(core, model, compiled);
            }
            catch (WasiNNException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"OpenVINO model load failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }

        public IBackendGraph LoadGraphByName(string name, ExecutionTarget target)
        {
            // The named-model resolver lives on
            // WasiNNConfiguration; the host calls LoadGraph after
            // looking the name up. OpenVINO has no internal
            // named-model registry, so route to NotFound and let
            // the WasiNNHost-level resolver do its job.
            throw new WasiNNException(
                ErrorCode.NotFound,
                "OpenVinoBackend has no internal named-model registry; "
                + "configure WasiNNConfiguration.NamedModelResolver "
                + "to map names to (encoding, builders) pairs.");
        }

        // Resolve the OpenVINO device-name string from the
        // guest's ExecutionTarget plus the host's typed options.
        // Typed options win when they're not Auto — embedders
        // that pinned a device at startup keep that pin. When
        // typed options are Auto, the guest's ExecutionTarget
        // chooses (CPU / GPU / NPU); TPU maps to NPU since
        // that's OpenVINO's analog.
        private string ChooseDeviceName(ExecutionTarget target)
        {
            if (_options.Device != OpenVinoDevice.Auto)
                return OpenVinoBackendOptions.DeviceName(_options.Device);

            switch (target)
            {
                case ExecutionTarget.CPU: return "CPU";
                case ExecutionTarget.GPU: return "GPU";
                case ExecutionTarget.TPU: return "NPU";
                default: return "AUTO";
            }
        }
    }
}
