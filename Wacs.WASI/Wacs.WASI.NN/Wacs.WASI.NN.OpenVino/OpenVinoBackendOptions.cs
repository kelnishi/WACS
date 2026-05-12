// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;

namespace Wacs.WASI.NN.OpenVino
{
    /// <summary>
    /// Typed configuration for <see cref="OpenVinoBackend"/> —
    /// primarily the OpenVINO device selection plus the
    /// arbitrary <c>compile_model</c> property bag (precision
    /// hints, thread counts, etc.). Sits next to the
    /// <see cref="OpenVinoBackend"/> default ctor: that ctor
    /// pulls these settings out of environment variables; the
    /// typed-options ctor is the in-process knob.
    ///
    /// <para>The default-constructed instance picks
    /// <see cref="OpenVinoDevice.Auto"/> — OpenVINO's AUTO
    /// plugin falls back across CPU / GPU / NPU based on what
    /// the runtime distribution ships. To pin a device:
    /// <c>WACS_WASINN_OPENVINO_DEVICE=cpu</c> (or <c>gpu</c>,
    /// <c>npu</c>, <c>auto</c>); or construct
    /// <see cref="OpenVinoBackend"/> with
    /// <c>new OpenVinoBackendOptions { Device = OpenVinoDevice.Cpu }</c>.</para>
    /// </summary>
    public sealed class OpenVinoBackendOptions
    {
        /// <summary>
        /// Target device for <c>compile_model</c>. Default is
        /// <see cref="OpenVinoDevice.Auto"/>, which lets the
        /// OpenVINO runtime pick across what's installed
        /// (typically CPU). Pin explicitly via this property or
        /// the <c>WACS_WASINN_OPENVINO_DEVICE</c> environment
        /// variable when the guest's
        /// <see cref="Types.ExecutionTarget"/> request isn't
        /// granular enough.
        /// </summary>
        public OpenVinoDevice Device { get; set; } = OpenVinoDevice.Auto;

        /// <summary>
        /// Extra key/value pairs forwarded into the
        /// <c>compile_model</c> properties dictionary — passes
        /// through any OpenVINO runtime property the host wants
        /// to set (e.g. <c>PERFORMANCE_HINT=LATENCY</c>,
        /// <c>INFERENCE_PRECISION_HINT=f16</c>,
        /// <c>NUM_STREAMS=AUTO</c>). Embedders that want
        /// fine-grained control over OpenVINO compile-time
        /// behavior populate this from their own configuration.
        /// </summary>
        public Dictionary<string, string> Properties { get; }
            = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// When the chosen device fails to compile (plugin
        /// missing, driver issue, unsupported op), retry with
        /// "CPU" instead of propagating the failure. Defaults
        /// to true — matches the ONNX backend's
        /// FallbackToCpu=true semantics so wasi-nn guests don't
        /// see a transient error just because the host's GPU
        /// plugin isn't installed. Set false for strict mode.
        /// </summary>
        public bool FallbackToCpu { get; set; } = true;

        /// <summary>
        /// Construct an options instance from environment
        /// variables. Reads <c>WACS_WASINN_OPENVINO_DEVICE</c>
        /// for the device name (case-insensitive:
        /// <c>auto</c>, <c>cpu</c>, <c>gpu</c>, <c>npu</c>),
        /// <c>WACS_WASINN_OPENVINO_PROPERTIES</c> for a
        /// comma-separated <c>KEY=VALUE</c> list forwarded into
        /// <see cref="Properties"/> (e.g.
        /// <c>PERFORMANCE_HINT=LATENCY,INFERENCE_PRECISION_HINT=f16</c>),
        /// and <c>WACS_WASINN_OPENVINO_FALLBACK_CPU</c> for the
        /// fallback switch (<c>0</c>/<c>false</c> disables it).
        /// Unrecognized device values leave the default
        /// (<see cref="OpenVinoDevice.Auto"/>).
        /// </summary>
        public static OpenVinoBackendOptions FromEnvironment()
        {
            var opts = new OpenVinoBackendOptions();
            var dev = Environment.GetEnvironmentVariable("WACS_WASINN_OPENVINO_DEVICE");
            if (!string.IsNullOrWhiteSpace(dev))
                opts.Device = ParseDevice(dev!);

            var props = Environment.GetEnvironmentVariable("WACS_WASINN_OPENVINO_PROPERTIES");
            if (!string.IsNullOrWhiteSpace(props))
            {
                foreach (var raw in props!.Split(new[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = raw.IndexOf('=');
                    if (eq <= 0 || eq == raw.Length - 1) continue;
                    var k = raw.Substring(0, eq).Trim();
                    var v = raw.Substring(eq + 1).Trim();
                    if (k.Length == 0 || v.Length == 0) continue;
                    opts.Properties[k] = v;
                }
            }

            var fallback = Environment.GetEnvironmentVariable("WACS_WASINN_OPENVINO_FALLBACK_CPU");
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                switch (fallback!.Trim().ToLowerInvariant())
                {
                    case "0":
                    case "false":
                    case "no":
                    case "off":
                        opts.FallbackToCpu = false;
                        break;
                }
            }

            return opts;
        }

        /// <summary>
        /// Map a typed <see cref="OpenVinoDevice"/> to the
        /// device-name string that <c>Core.compile_model</c>
        /// accepts. Centralized here so the device-string
        /// vocabulary is in one place — OpenVINO recognizes
        /// many more names (HETERO, BATCH, MULTI, …); only the
        /// four basic ones are exposed through the typed enum,
        /// embedders that need the rest set
        /// <see cref="Properties"/> on top of an Auto pick.
        /// </summary>
        public static string DeviceName(OpenVinoDevice device) => device switch
        {
            OpenVinoDevice.Cpu => "CPU",
            OpenVinoDevice.Gpu => "GPU",
            OpenVinoDevice.Npu => "NPU",
            _ => "AUTO",
        };

        private static OpenVinoDevice ParseDevice(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": return OpenVinoDevice.Auto;
                case "cpu": return OpenVinoDevice.Cpu;
                case "gpu": return OpenVinoDevice.Gpu;
                case "npu": return OpenVinoDevice.Npu;
                default: return OpenVinoDevice.Auto;
            }
        }
    }
}
