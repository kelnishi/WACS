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

        /// <summary>
        /// Historic selector for the data-segment storage shape. <b>Advisory
        /// only since the RVA migration</b> — every value now produces the
        /// same on-disk shape (bytes ride RVA-mapped through
        /// <c>__WACSAotData.Segment_*</c> and <c>__WACSInit.Data</c>, surfaced
        /// zero-copy via <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c>).
        /// Preserved so existing <c>--data-storage</c> CLI invocations keep
        /// parsing; future releases may drop the option.
        /// </summary>
        public DataSegmentStorage DataStorage { get; set; } = DataSegmentStorage.CompressedResource;

        /// <summary>
        /// Selects the Module ctor emission shape. Default <see cref="EmissionTarget.Standard"/>
        /// emits a ctor that calls <see cref="InitializationHelper.InitializeFromEmbedded"/>
        /// against an RVA-mapped <c>ReadOnlySpan&lt;byte&gt;</c> over the codec blob
        /// (works for in-process and cross-process load equally).
        /// <see cref="EmissionTarget.AotLinked"/> emits a leaner ctor that
        /// constructs the <see cref="ThinContext"/> directly from inlined IL constants,
        /// targeting whole-program NativeAOT consumers where the codec machinery is
        /// pure overhead and would just bloat the native binary.
        ///
        /// <para>Currently, AotLinked emission only supports modules with no memories,
        /// tables, globals, or data segments (e.g. compute-only wasm). Modules that
        /// declare any of these fall back to <c>Standard</c> with a diagnostic.
        /// Coverage will grow incrementally.</para>
        /// </summary>
        public EmissionTarget Emission { get; set; } = EmissionTarget.Standard;

        /// <summary>
        /// Optional override for the generated assembly's logical name
        /// (i.e. the value of <c>Assembly.GetName().Name</c>). When null,
        /// <c>ModuleTranspiler</c> appends a process-unique <c>_&lt;N&gt;</c>
        /// suffix to the namespace + module name to avoid type collisions
        /// across overlapping in-process transpilations — fine for the
        /// `transpiler` runtime path but brittle for static linking.
        ///
        /// <para>When set, the transpiler uses this string verbatim and
        /// skips the suffix. The matching saved <c>.dll</c> file should
        /// be named <c>&lt;AssemblyName&gt;.dll</c> on disk so ILC's
        /// resolver can find it via static <c>&lt;Reference&gt;</c> from
        /// a NativeAOT consumer.</para>
        ///
        /// <para>Required for the wacs-aot whole-program build path; not
        /// used by the in-process or load-via-AssemblyLoadContext paths.</para>
        /// </summary>
        public string? AssemblyName { get; set; }

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
    /// Module-class emission shape. See <see cref="TranspilerOptions.Emission"/>.
    /// </summary>
    public enum EmissionTarget
    {
        /// <summary>
        /// Module ctor goes through <see cref="InitializationHelper.InitializeFromEmbedded"/>
        /// against a codec-encoded byte[] holder. Works for both in-process and
        /// cross-process load (the helper branches internally). The default.
        /// </summary>
        Standard,

        /// <summary>
        /// Module ctor constructs <see cref="ThinContext"/> directly from inlined
        /// IL constants — no <c>__WACSInit</c> holder, no <see cref="InitDataCodec"/>
        /// call, no <see cref="InitRegistry"/> dependency. Allows a NativeAOT
        /// consumer's trimmer to dead-strip the codec/registry machinery from the
        /// final native binary. Only the AOT-linked workflow needs this — the
        /// in-process and saved-DLL-via-AssemblyLoadContext paths still want
        /// Standard for their cross-process safety.
        /// </summary>
        AotLinked,
    }

    /// <summary>
    /// Historic strategy selector for storing WASM data segments. <b>Advisory
    /// only as of the RVA migration</b> — every value resolves to the same
    /// shape today: bytes ride RVA-mapped through <c>__WACSAotData.Segment_*</c>
    /// (active segments under AotLinked emission) and <c>__WACSInit.Data</c>
    /// (the codec blob), and reach the runtime zero-copy via
    /// <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c>. The enum is preserved so
    /// existing CLI flags (<c>--data-storage compressed|raw|static</c>) and
    /// <see cref="TranspilerOptions.DataStorage"/> defaults continue to
    /// parse; the runtime / on-disk shape no longer varies by selection.
    /// </summary>
    public enum DataSegmentStorage
    {
        /// <summary>
        /// Historic Brotli-compressed-resource selector. <b>Advisory only —</b>
        /// see <see cref="DataSegmentStorage"/> for the unified RVA path.
        /// </summary>
        CompressedResource,

        /// <summary>
        /// Historic uncompressed-resource selector. <b>Advisory only —</b>
        /// see <see cref="DataSegmentStorage"/> for the unified RVA path.
        /// </summary>
        RawResource,

        /// <summary>
        /// Historic <c>static readonly byte[]</c>-fields selector.
        /// <b>Advisory only —</b> see <see cref="DataSegmentStorage"/> for
        /// the unified RVA path.
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
