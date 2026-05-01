// Copyright 2025 Kelvin Nishikawa
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

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// SIMD implementation strategy selection.
    /// </summary>
    public enum SimdStrategy
    {
        /// <summary>
        /// Dispatch all SIMD ops through the interpreter's Execute methods.
        /// Correct but slow — every op marshals through ExecContext.OpStack.
        /// </summary>
        InterpreterDispatch,

        /// <summary>
        /// Use spec-compliant scalar helper methods (element-wise V128 operations).
        /// Bypasses the interpreter but does not use hardware SIMD.
        /// This is the reference implementation for correctness testing.
        /// </summary>
        ScalarReference,

        /// <summary>
        /// Use Vector128&lt;T&gt; hardware intrinsics where available.
        /// Falls back to scalar for ops without direct CLR support.
        /// Reports a diagnostic when fallback occurs.
        /// </summary>
        HardwareIntrinsics,
    }

    /// <summary>
    /// Configuration options for the AOT transpiler.
    /// Flows through ModuleTranspiler → FunctionCodegen → Emitters.
    /// </summary>
    public class TranspilerOptions
    {
        /// <summary>SIMD implementation strategy.</summary>
        public SimdStrategy Simd { get; set; } = SimdStrategy.ScalarReference;

        /// <summary>
        /// When true, emit the CIL `tail.` prefix for return_call instructions.
        /// WASM return_call semantics require true tail calls — without this,
        /// recursive tail calls exhaust the CLR stack. The CLR honors tail. when
        /// the call site meets the preconditions (matching signatures, no try
        /// frames, immediate ret after call). Our sibling-call emission meets
        /// these, so tail calls are enabled by default for spec compliance.
        /// </summary>
        public bool EmitTailCallPrefix { get; set; } = true;

        /// <summary>
        /// Maximum function body size (in instructions) to attempt transpilation.
        /// Very large functions can cause excessive IL emission time.
        /// 0 = no limit.
        /// </summary>
        public int MaxFunctionSize { get; set; } = 0;

        /// <summary>How data segments are stored in the transpiled assembly.</summary>
        public DataSegmentStorage DataStorage { get; set; } = DataSegmentStorage.CompressedResource;

        /// <summary>
        /// GC type checking capabilities to enable in transpiled assemblies.
        /// Layer 0 (CLR inheritance) is always active. These flags enable additional layers.
        /// </summary>
        public TranspilerCapabilities GcTypeChecking { get; set; } = TranspilerCapabilities.None;

        /// <summary>
        /// Pre-built resolver derived from <see cref="HostPackages"/>.
        /// The component transpiler builds this once per
        /// <c>TranspileSingleModule</c> call (eagerly walking each
        /// host package's <c>[WitSource]</c> interfaces) and stashes
        /// it here so the call-site emitter can branch without
        /// rebuilding the index per function. Null when host-package
        /// resolution doesn't apply (core-wasm CLI path, or no
        /// packages supplied).
        /// </summary>
        public Component.HostPackageResolver? Resolver { get; set; }

        /// <summary>
        /// Transient per-transpilation map: import function index →
        /// resolved binding. Built by <c>ModuleTranspiler.Transpile</c>
        /// from <see cref="Resolver"/> + the module's import section,
        /// then read by <c>CallEmitter.EmitImportCall</c> to branch
        /// each guest <c>call $import</c> between direct-linked IL
        /// (resolver hit) and the legacy delegate-table dispatch
        /// (miss). Reset at the start of each
        /// <c>ModuleTranspiler.Transpile</c> call — concurrent uses
        /// of the same <see cref="TranspilerOptions"/> instance
        /// across overlapping transpilations are not supported.
        /// </summary>
        public IReadOnlyDictionary<int, Component.HostPackageResolver.Binding>?
            ResolverImportBindings { get; set; }

        /// <summary>
        /// Host-package assemblies whose <c>[WitSource]</c>-tagged
        /// interfaces resolve component-mode imports at transpile time.
        /// For each guest <c>call $import</c>, the component transpiler
        /// looks up a matching binding in this list and emits inline
        /// IL (typed <c>callvirt</c>) instead of routing through the
        /// runtime's delegate table. Missing or arity-mismatched
        /// imports become a build-time error rather than an
        /// instantiation-time one.
        ///
        /// <para>Empty by default. The CLI sets this from
        /// <c>--host-package</c> (repeatable) and <c>--wasip2</c>.
        /// Programmatic callers (component tests) can populate it
        /// directly. Has no effect on core-wasm transpilation; only
        /// the component path consumes it.</para>
        /// </summary>
        public IReadOnlyList<Assembly> HostPackages { get; set; }
            = Array.Empty<Assembly>();
    }

    /// <summary>
    /// Strategy for storing WASM data segments in the transpiled assembly.
    /// </summary>
    public enum DataSegmentStorage
    {
        /// <summary>
        /// Data segments embedded as Brotli-compressed assembly resources.
        /// Decompressed at module instantiation. Smallest assembly size.
        /// </summary>
        CompressedResource,

        /// <summary>
        /// Data segments embedded as uncompressed assembly resources.
        /// Fastest instantiation (no decompression). Moderate assembly size.
        /// </summary>
        RawResource,

        /// <summary>
        /// Data segments emitted as static readonly byte[] fields on the Functions class.
        /// Loaded into managed heap at first access. Largest assembly size.
        /// No resource API needed — simplest for debugging.
        /// </summary>
        StaticArrays,
    }

    /// <summary>
    /// Severity levels for transpiler diagnostics.
    /// </summary>
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// A diagnostic message emitted during transpilation.
    /// </summary>
    public class TranspilerDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public string? FunctionName { get; }
        public string? Opcode { get; }

        public TranspilerDiagnostic(DiagnosticSeverity severity, string message,
            string? functionName = null, string? opcode = null)
        {
            Severity = severity;
            Message = message;
            FunctionName = functionName;
            Opcode = opcode;
        }

        public override string ToString() =>
            $"[{Severity}] {(FunctionName != null ? $"{FunctionName}: " : "")}{Message}" +
            $"{(Opcode != null ? $" (opcode: {Opcode})" : "")}";
    }

    /// <summary>
    /// Collects diagnostics during transpilation.
    /// Thread-safe for use during parallel function emission (future).
    /// </summary>
    public class DiagnosticCollector
    {
        private readonly List<TranspilerDiagnostic> _diagnostics = new();
        private readonly object _lock = new();

        public void Add(DiagnosticSeverity severity, string message,
            string? functionName = null, string? opcode = null)
        {
            lock (_lock)
            {
                _diagnostics.Add(new TranspilerDiagnostic(severity, message, functionName, opcode));
            }
        }

        public void Info(string message, string? functionName = null, string? opcode = null) =>
            Add(DiagnosticSeverity.Info, message, functionName, opcode);

        public void Warning(string message, string? functionName = null, string? opcode = null) =>
            Add(DiagnosticSeverity.Warning, message, functionName, opcode);

        public void Error(string message, string? functionName = null, string? opcode = null) =>
            Add(DiagnosticSeverity.Error, message, functionName, opcode);

        public IReadOnlyList<TranspilerDiagnostic> Diagnostics
        {
            get { lock (_lock) { return _diagnostics.ToArray(); } }
        }

        public int WarningCount
        {
            get { lock (_lock) { return _diagnostics.FindAll(d => d.Severity == DiagnosticSeverity.Warning).Count; } }
        }

        public int ErrorCount
        {
            get { lock (_lock) { return _diagnostics.FindAll(d => d.Severity == DiagnosticSeverity.Error).Count; } }
        }
    }
}
