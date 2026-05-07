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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT.Emitters;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Result of transpiling a module. Lifecycle: built into a
    /// <see cref="PersistedAssemblyBuilder"/>, then "baked" (saved to a
    /// <see cref="MemoryStream"/> and reloaded via
    /// <see cref="AssemblyLoadContext"/>) before runtime invocation.
    /// <para>
    /// <b>Pre-bake</b>: types are <see cref="PersistedAssemblyBuilder"/>
    /// metadata-only. Safe for emit-time inspection (Namespace, GetMethod,
    /// GetField, IL-emit Ldtoken) and for adding more types via
    /// <see cref="ModuleBuilder"/>. NOT safe for <see cref="Activator.CreateInstance"/>
    /// or any code path that needs a runtime-loaded type.
    /// </para>
    /// <para>
    /// <b>Post-bake</b>: <see cref="Assembly"/>, <see cref="ModuleClass"/>,
    /// <see cref="ExportsInterface"/>, <see cref="ImportsInterface"/>,
    /// <see cref="FunctionsType"/>, <see cref="Methods"/>, and
    /// <see cref="FunctionMethodMap"/> are remapped to the loaded assembly's
    /// equivalents. <see cref="ModuleBuilder"/> is then frozen — adding new
    /// types post-bake is not supported.
    /// </para>
    /// <para>
    /// Public type/method accessors auto-bake on first access. Emitters that
    /// run between <c>Transpile()</c> and <c>SaveAssembly()</c> (e.g.
    /// <see cref="MainEntryEmitter"/>) read pre-bake metadata via the
    /// <c>*Builder</c>-suffixed accessors to avoid prematurely freezing the
    /// builder.
    /// </para>
    /// </summary>
    public class TranspilationResult
    {
        // Backing state. Replaced in-place by Bake(); _baked guards.
        private bool _baked;
        private byte[]? _bakedBytes;
        private Assembly _assembly;
        private Type _functionsType;
        private Type? _exportsInterface;
        private Type? _importsInterface;
        private Type? _moduleClass;
        private MethodInfo[] _methods;
        private Dictionary<int, MethodInfo> _functionMethodMap;

        public Assembly Assembly { get { Bake(); return _assembly; } }
        public Type FunctionsType { get { Bake(); return _functionsType; } }
        public MethodInfo[] Methods { get { Bake(); return _methods; } }
        public ModuleMetadata.Manifest Manifest { get; }

        /// <summary>
        /// Maps wasm function index (within the module's locally-defined functions)
        /// to the corresponding MethodInfo in the transpiled assembly.
        /// </summary>
        public IReadOnlyDictionary<int, MethodInfo> FunctionMethodMap
        {
            get { Bake(); return _functionMethodMap; }
        }

        public int TranspiledCount => Manifest.TranspiledCount;
        public int FallbackCount => Manifest.FallbackCount;

        /// <summary>Diagnostics emitted during transpilation.</summary>
        public IReadOnlyList<TranspilerDiagnostic> Diagnostics { get; }

        /// <summary>Generated interface for module exports. Null if no exports.</summary>
        public Type? ExportsInterface { get { Bake(); return _exportsInterface; } }

        /// <summary>Generated interface for module imports. Null if no imports.</summary>
        public Type? ImportsInterface { get { Bake(); return _importsInterface; } }

        /// <summary>Generated Module class (implements IExports, accepts IImports).</summary>
        public Type? ModuleClass { get { Bake(); return _moduleClass; } }

        // === Pre-bake (build-time) accessors for in-Lib emitters ===
        // These return the metadata-only PersistedTypeBuilder.CreateType()
        // results without triggering a Save+Load. Safe to use for IL emit
        // and reflective metadata reads (Namespace, GetMethod, GetField).
        // Accessing these does NOT freeze ModuleBuilder, so emitters can
        // continue calling DefineType.

        internal Type FunctionsTypeBuilder => _functionsType;
        internal Type? ModuleClassBuilder => _moduleClass;
        internal Type? ExportsInterfaceBuilder => _exportsInterface;
        internal Type? ImportsInterfaceBuilder => _importsInterface;

        /// <summary>Export method metadata (name, type, index).</summary>
        public IReadOnlyList<InterfaceMethod> ExportMethods { get; }

        /// <summary>Import method metadata (name, type, index).</summary>
        public IReadOnlyList<InterfaceMethod> ImportMethods { get; }

        /// <summary>Function types for all functions in module index space (imports + locals).</summary>
        public FunctionType[] AllFunctionTypes { get; }

        /// <summary>ID into InitRegistry for this module's initialization data.</summary>
        public int InitDataId { get; set; } = -1;

        /// <summary>
        /// The still-open <see cref="ModuleBuilder"/> the transpiler wrote into,
        /// kept live so callers can define additional types (e.g.
        /// <see cref="MainEntryEmitter"/> for <c>--emit-main</c>) before the
        /// assembly is persisted to disk. Frozen once <see cref="Bake"/> runs.
        /// </summary>
        public ModuleBuilder ModuleBuilder { get; }

        public TranspilationResult(
            Assembly assembly,
            ModuleBuilder moduleBuilder,
            Type functionsType,
            MethodInfo[] methods,
            ModuleMetadata.Manifest manifest,
            Dictionary<int, MethodInfo> functionMethodMap,
            IReadOnlyList<TranspilerDiagnostic> diagnostics,
            Type? exportsInterface,
            Type? importsInterface,
            Type? moduleClass,
            IReadOnlyList<InterfaceMethod> exportMethods,
            IReadOnlyList<InterfaceMethod> importMethods,
            FunctionType[]? allFunctionTypes = null)
        {
            _assembly = assembly;
            ModuleBuilder = moduleBuilder;
            _functionsType = functionsType;
            _methods = methods;
            Manifest = manifest;
            _functionMethodMap = functionMethodMap;
            Diagnostics = diagnostics;
            _exportsInterface = exportsInterface;
            _importsInterface = importsInterface;
            _moduleClass = moduleClass;
            ExportMethods = exportMethods;
            ImportMethods = importMethods;
            AllFunctionTypes = allFunctionTypes ?? Array.Empty<FunctionType>();
        }

        // Captured at end of Transpile so Bake can re-evaluate element
        // segments whose initializers depend on emitted GC types being
        // runtime-instantiable (only true after Save+Load).
        private GcTypeEmitter? _gcTypeEmitterForReeval;
        private List<(int segId, Wacs.Core.Types.Expression[] inits)>? _pendingElemReeval;

        internal void SetPendingElemReeval(
            GcTypeEmitter gcTypeEmitter,
            List<(int segId, Wacs.Core.Types.Expression[] inits)> pending)
        {
            _gcTypeEmitterForReeval = gcTypeEmitter;
            _pendingElemReeval = pending;
        }

        /// <summary>
        /// Save the in-memory <see cref="PersistedAssemblyBuilder"/> to a
        /// <see cref="MemoryStream"/>, load that stream into the default
        /// <see cref="AssemblyLoadContext"/>, and remap public type/method
        /// accessors onto the loaded assembly. Idempotent.
        /// <para>
        /// Triggered automatically on first access to public type accessors
        /// (Assembly, ModuleClass, FunctionsType, Methods,
        /// FunctionMethodMap, ExportsInterface, ImportsInterface) and by
        /// <see cref="SaveAssembly"/>. Callers that need to add more types
        /// to <see cref="ModuleBuilder"/> must do so before triggering a bake.
        /// </para>
        /// </summary>
        public void Bake()
        {
            if (_baked) return;

            if (_assembly is PersistedAssemblyBuilder pab)
            {
                using var ms = new MemoryStream();
                pab.Save(ms);
                _bakedBytes = ms.ToArray();
                using var loadStream = new MemoryStream(_bakedBytes, writable: false);
                _assembly = AssemblyLoadContext.Default.LoadFromStream(loadStream);
            }
            // else: already a runtime-loaded Assembly (e.g. test harness
            // injected one). Skip the round-trip but still remap.

            _functionsType = ResolveType(_functionsType)!;
            _moduleClass = ResolveType(_moduleClass);
            _exportsInterface = ResolveType(_exportsInterface);
            _importsInterface = ResolveType(_importsInterface);

            for (int i = 0; i < _methods.Length; i++)
                _methods[i] = ResolveMethod(_methods[i])!;

            var remappedMap = new Dictionary<int, MethodInfo>(_functionMethodMap.Count);
            foreach (var kv in _functionMethodMap)
                remappedMap[kv.Key] = ResolveMethod(kv.Value)!;
            _functionMethodMap = remappedMap;

            // Remap GcTypeRegistry entries owned by this transpilation
            // to the loaded assembly's runtime types — runtime helpers
            // (ConvertStoreArray / ConvertStoreStruct) call
            // Activator.CreateInstance against the registered Type and
            // need a runtime, not a metadata-only, Type.
            if (InitDataId >= 0)
                GcTypeRegistry.RemapToLoadedTypes(InitDataId, _assembly);

            // Element segments whose initializers materialize GC objects
            // could not be evaluated at transpile time (the emitted CLR
            // class was metadata-only). With the loaded assembly online,
            // remap the GcTypeEmitter's emitted types and replay the
            // captured initializers.
            if (_pendingElemReeval != null && _gcTypeEmitterForReeval != null
                && _pendingElemReeval.Count > 0)
            {
                _gcTypeEmitterForReeval.RemapEmittedToLoadedTypes(_assembly);
                foreach (var (segId, inits) in _pendingElemReeval)
                {
                    var values = new Value[inits.Length];
                    for (int i = 0; i < inits.Length; i++)
                        values[i] = ModuleTranspiler.EvaluateElemExprForBake(
                            inits[i], _gcTypeEmitterForReeval);
                    ModuleInit.UpdateElemSegment(segId, values);
                }
                _pendingElemReeval = null;
                _gcTypeEmitterForReeval = null;
            }

            _baked = true;
        }

        private Type? ResolveType(Type? metadataType)
        {
            if (metadataType == null) return null;
            return _assembly.GetType(metadataType.FullName!) ?? metadataType;
        }

        private MethodInfo? ResolveMethod(MethodInfo? metadataMethod)
        {
            if (metadataMethod == null) return null;
            var dt = ResolveType(metadataMethod.DeclaringType);
            if (dt == null) return metadataMethod;
            // Match by name + parameter signature; transpiled functions
            // typically have unique names, but the type may also expose
            // overloads (e.g. ctors), so be precise.
            var paramTypes = metadataMethod.GetParameters()
                .Select(p => p.ParameterType.FullName!)
                .ToArray();
            return dt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Static | BindingFlags.Instance |
                                 BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == metadataMethod.Name
                    && m.GetParameters().Length == paramTypes.Length
                    && m.GetParameters()
                        .Select(p => p.ParameterType.FullName)
                        .SequenceEqual(paramTypes))
                ?? dt.GetMethod(metadataMethod.Name) ?? metadataMethod;
        }

        /// <summary>
        /// Persist the transpiled assembly to disk as a standalone PE image.
        /// Uses <see cref="PersistedAssemblyBuilder.Save(Stream)"/> via
        /// <see cref="Bake"/>; the saved bytes are valid for
        /// <c>Assembly.LoadFrom</c> in any consumer process.
        /// </summary>
        /// <param name="path">Output path (typically ending in <c>.dll</c>).</param>
        public void SaveAssembly(string path)
        {
            Bake();
            // PAB stamps the saved DLL's corelib AssemblyRef as
            // `System.Private.CoreLib` (the runtime impl identity, not
            // the contract `System.Runtime` that the C# compiler sees in
            // the standard ref pack). Consumer csprojs that reference
            // the saved .dll trip CS0012 at compile time without an
            // intervention. Post-process a copy of the baked bytes
            // (never the in-process `_bakedBytes`, so the loaded
            // `_assembly` keeps the impl-corelib identity) to swap the
            // corelib AssemblyRef from `System.Private.CoreLib` to
            // `System.Runtime`. See CoreLibAssemblyRefRewriter for the
            // full rationale, the byte-level edit details, the known
            // limitation (generic-instantiation FieldRefs in isolated
            // ALCs), and the conditions under which this hack can be
            // removed.
            var diskBytes = (byte[])_bakedBytes!.Clone();
            CoreLibAssemblyRefRewriter.RewritePrivateCoreLibToSystemRuntime(diskBytes);
            File.WriteAllBytes(path, diskBytes);
        }
    }

    /// <summary>
    /// Orchestrates the transpilation of a WebAssembly module into a .NET assembly.
    ///
    /// Two-pass approach:
    ///   Pass 1: Create MethodBuilder stubs for every function with correct signatures.
    ///   Pass 2: Emit IL bodies (or fallback stubs) for each function.
    /// </summary>
    public class ModuleTranspiler
    {
        private static int _assemblyCounter;
        private readonly string _namespace;
        private readonly TranspilerOptions _options;

        public ModuleTranspiler(string @namespace = "Wacs.Transpiled", TranspilerOptions? options = null)
        {
            _namespace = @namespace;
            _options = options ?? new TranspilerOptions();
        }

        /// <summary>
        /// Transpile all functions in a module instance to a dynamic .NET assembly.
        /// </summary>
        public TranspilationResult Transpile(
            ModuleInstance moduleInst,
            WasmRuntime runtime,
            string moduleName = "WasmModule")
        {
            // Each transpilation gets a unique assembly name to prevent type conflicts
            // across multiple dynamic assemblies (e.g., WasmStruct_0 in different modules).
            // Callers that need a predictable name (whole-program AOT linkage, where the
            // saved .dll is statically referenced from a consumer csproj) set
            // TranspilerOptions.AssemblyName to skip the suffix.
            AssemblyName assemblyName;
            if (!string.IsNullOrEmpty(_options.AssemblyName))
            {
                assemblyName = new AssemblyName(_options.AssemblyName);
            }
            else
            {
                var uniqueId = System.Threading.Interlocked.Increment(ref _assemblyCounter);
                assemblyName = new AssemblyName($"{_namespace}.{moduleName}_{uniqueId}");
            }
            // PersistedAssemblyBuilder is the .NET 9+ replacement for the
            // legacy `AssemblyBuilderAccess.Save` mode (removed in .NET Core).
            // Built types are metadata-only until the assembly is saved and
            // loaded back; TranspilationResult.Bake() handles that round-trip
            // before runtime invocation. Critical: PersistedAssemblyBuilder
            // supports DefineInitializedData (RVA-mapped fields) end-to-end
            // through Save, which is what unblocks zero-copy WASM data
            // segments — Lokad.ILPack 0.3.1 used to NRE on that path.
            //
            // PAB stamps the saved DLL's corelib reference as
            // `System.Private.CoreLib` because we hand it
            // `typeof(object).Assembly`. The MLC-corelib alternative the
            // PAB docs recommend requires every type passed to PAB to come
            // from MLC (not impl `typeof(...)`), which would mean
            // refactoring every IL-emit call site. Instead, we keep impl
            // types here and post-process the saved DLL via
            // CoreLibAssemblyRefRewriter at SaveAssembly time, swapping
            // the AssemblyRef for `System.Private.CoreLib` to
            // `System.Runtime`. The runtime treats them as equivalent
            // through type forwards, so semantics are preserved — only
            // the C# compiler's reference-pack lookup needs the rename.
            var assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

            var typeBuilder = moduleBuilder.DefineType(
                $"{_namespace}.{moduleName}.Functions",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

            // Collect all wasm functions and count imports
            var wasmFunctions = new List<FunctionInstance>();
            int importCount = 0;
            bool foundLocal = false;
            foreach (var funcAddr in moduleInst.FuncAddrs)
            {
                var func = runtime.GetFunction(funcAddr);
                if (func is FunctionInstance fi && fi.Module == moduleInst)
                {
                    foundLocal = true;
                    wasmFunctions.Add(fi);
                }
                else if (!foundLocal)
                {
                    importCount++;
                }
            }

            // Pre-resolve all function types (imports + locals) for call site resolution
            var allFunctionTypes = new FunctionType[importCount + wasmFunctions.Count];
            int fIdx = 0;
            foreach (var funcAddr in moduleInst.FuncAddrs)
            {
                var func = runtime.GetFunction(funcAddr);
                allFunctionTypes[fIdx++] = func.Type;
                if (fIdx >= allFunctionTypes.Length) break;
            }

            var diagnostics = new DiagnosticCollector();

            // === Analyze data segments ===
            var dataEmitter = new DataSegmentEmitter(moduleInst.Repr, _options.DataStorage, diagnostics);
            dataEmitter.Analyze();
            // Register ALL segment data for runtime access (dynamic assemblies).
            // Track the base segment ID so PrepareInitData can compute absolute IDs.
            int dataSegmentBaseId = -1;
            for (int s = 0; s < dataEmitter.Segments.Length; s++)
            {
                int id = ModuleInit.RegisterDataSegment(dataEmitter.Segments[s].Data);
                if (s == 0) dataSegmentBaseId = id;
            }

            // === Pass 0a: Generate typed interfaces for exports and imports ===
            var interfaceGen = new InterfaceGenerator(
                moduleBuilder, $"{_namespace}.{moduleName}",
                moduleInst.Repr, moduleInst, runtime, importCount);
            interfaceGen.Generate();

            // Mark the assembly with [WacsTranspiledImports("Ns.IImports")]
            // so WACS.HostBindings.SourceGen can find the IImports interface
            // from a consumer's compilation references and synthesize an
            // adapter. Only emit when there are actually imports — modules
            // with no IImports interface have nothing for the source gen
            // to adapt. String-based (not typeof(T)) so Lokad.ILPack can
            // serialize the attribute blob without resolving a Type that
            // lives in the still-being-saved assembly.
            if (interfaceGen.ImportsInterface != null)
            {
                var attrCtor = typeof(Wacs.HostBindings.WacsTranspiledImportsAttribute)
                    .GetConstructor(new[] { typeof(string) })!;
                assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                    attrCtor,
                    new object[] { interfaceGen.ImportsInterface!.FullName! }));

                // Also stamp the (methodName, wasmModule, wasmName) table
                // for source-gen binding match. Hoisted to assembly level
                // because Lokad.ILPack doesn't preserve method-level
                // custom attributes.
                if (interfaceGen.ImportMethods.Count > 0)
                {
                    var entries = new System.Collections.Generic.List<(string, string, string)>();
                    foreach (var im in interfaceGen.ImportMethods)
                    {
                        if (im.WasmImportModule != null && im.WasmImportEntity != null)
                            entries.Add((im.Name, im.WasmImportModule, im.WasmImportEntity));
                    }
                    if (entries.Count > 0)
                    {
                        var mapping = Wacs.HostBindings.WacsImportNamesAttribute.Encode(entries);
                        var namesCtor = typeof(Wacs.HostBindings.WacsImportNamesAttribute)
                            .GetConstructor(new[] { typeof(string) })!;
                        assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                            namesCtor, new object[] { mapping }));
                    }
                }
            }

            // === Pass 0a.1: Resolve direct-linked host imports ===
            // For each import method, query the resolver (if any) for a
            // matching (module, entity) binding. Resolved imports get
            // direct-linked IL at call sites (bypassing the runtime
            // delegate table); unresolved imports keep the legacy
            // dispatch. Reset the transient map at the start of each
            // transpile call so a stale binding from a previous run
            // never leaks across.
            _options.ResolverImportBindings = null;
            if (_options.Resolver != null
                && interfaceGen.ImportMethods.Count > 0)
            {
                var bindings = new Dictionary<int, Component.HostPackageResolver.Binding>();
                foreach (var im in interfaceGen.ImportMethods)
                {
                    if (im.WasmImportModule == null
                        || im.WasmImportEntity == null) continue;
                    if (_options.Resolver.TryResolve(
                        im.WasmImportModule, im.WasmImportEntity,
                        out var b))
                    {
                        bindings[im.FuncIndex] = b;
                    }
                }
                if (bindings.Count > 0)
                    _options.ResolverImportBindings = bindings;
            }

            // === Pass 0b: Emit CLR types for WASM struct/array definitions ===
            var gcTypeEmitter = new GcTypeEmitter(moduleBuilder, $"{_namespace}.{moduleName}", moduleInst.Types);
            gcTypeEmitter.EmitTypes();

            // Register element segments for standalone table.init.
            // Deferred until AFTER gcTypeEmitter.EmitTypes so the evaluator can
            // look up emitted CLR types when an initializer constructs GC
            // objects (array.new / array.new_fixed / array.new_default).
            // Under PersistedAssemblyBuilder the emitted CLR types are
            // metadata-only at this point — array.new evaluation can't yet
            // call Activator.CreateInstance on them. Capture (segId,
            // initializers) for any segment whose evaluation depends on
            // emitted types so TranspilationResult.Bake can re-evaluate
            // post-Save+Load against the runtime types.
            int elemSegmentBaseId = -1;
            var pendingElemReeval = new List<(int segId, Wacs.Core.Types.Expression[] inits)>();
            for (int e = 0; e < moduleInst.Repr.Elements.Length; e++)
            {
                var elem = moduleInst.Repr.Elements[e];
                var values = new Value[elem.Initializers.Length];
                bool needsReeval = false;
                for (int i = 0; i < elem.Initializers.Length; i++)
                {
                    var expr = elem.Initializers[i];
                    values[i] = EvaluateElemExpr(expr, gcTypeEmitter);
                    if (HasGcConstructor(expr)) needsReeval = true;
                }
                int id = ModuleInit.RegisterElemSegment(values);
                if (e == 0) elemSegmentBaseId = id;
                if (needsReeval) pendingElemReeval.Add((id, elem.Initializers));
            }

            // === Pass 1: Create method stubs ===
            var methodBuilders = new MethodBuilder[wasmFunctions.Count];
            for (int i = 0; i < wasmFunctions.Count; i++)
            {
                var funcInst = wasmFunctions[i];
                var funcType = funcInst.Type;
                methodBuilders[i] = CreateMethodStub(typeBuilder, funcInst, funcType, i);
            }

            // === Pass 2: Emit IL bodies ===
            var manifest = new ModuleMetadata.Manifest
            {
                ModuleName = moduleName,
                Namespace = _namespace,
                FunctionsTypeName = $"{_namespace}.{moduleName}.Functions"
            };

            for (int i = 0; i < wasmFunctions.Count; i++)
            {
                var funcInst = wasmFunctions[i];
                var mb = methodBuilders[i];
                var codegen = new FunctionCodegen(mb, funcInst, wasmFunctions.ToArray(), methodBuilders, importCount, gcTypeEmitter, allFunctionTypes, _options, diagnostics);

                bool emitted = codegen.TryEmit();

                if (!emitted)
                {
                    EmitFallbackBody(mb, importCount + i, funcInst.Type);
                }

                manifest.Functions.Add(new ModuleMetadata.FunctionEntry
                {
                    Index = i,
                    MethodName = mb.Name,
                    IsTranspiled = emitted,
                    ExportName = funcInst.IsExport ? funcInst.Name : null,
                    RejectionReason = emitted ? null : codegen.LastRejectionReason
                });

                if (emitted)
                    manifest.TranspiledCount++;
                else
                    manifest.FallbackCount++;
            }

            // === Generate Module class (implements IExports, accepts IImports) ===
            var moduleClassGen = new ModuleClassGenerator(
                moduleBuilder, $"{_namespace}.{moduleName}",
                moduleInst.Repr, interfaceGen, typeBuilder, methodBuilders, importCount,
                dataEmitter, dataSegmentBaseId >= 0 ? dataSegmentBaseId : 0,
                elemSegmentBaseId >= 0 ? elemSegmentBaseId : 0,
                allFunctionTypes, _options);
            // Populate initData BEFORE Generate(): the generator's
            // EmitEmbeddedInitData takes a snapshot of InitRegistry.Get(id)
            // and writes it into the emitted byte[] resource. Anything we
            // mutate on the data object after Generate() runs won't make
            // it into the persisted .dll (in-process still works because
            // InitRegistry holds the live object, but cross-process sees
            // the pre-mutation snapshot). Ordering: prepare → populate all
            // fields → generate.
            moduleClassGen.PrepareInitData();

            // Resolve array element ValType for each GC global init so
            // array.new_default can seed Value[] slots with proper null refs
            // (default(Value) has Type=Undefined and reads as a live non-null
            // funcref at runtime, breaking call_indirect trap semantics).
            if (moduleClassGen.InitDataId >= 0)
            {
                var idata = InitRegistry.Get(moduleClassGen.InitDataId);
                foreach (var gi in idata.GcGlobalInits)
                {
                    if (!moduleInst.Types.Contains((TypeIdx)gi.TypeIndex)) continue;
                    if (moduleInst.Types[(TypeIdx)gi.TypeIndex].Expansion is ArrayType at)
                        gi.ElementValType = (int)at.ElementType.StorageType;
                }
            }

            // Populate function type hashes for runtime ref.cast/ref.test on funcrefs.
            // Each function's hash is the DefType.GetHashCode() (SubType.ComputedHash)
            // for equi-recursive type comparison.
            int initDataId = moduleClassGen.InitDataId;
            if (initDataId >= 0)
            {
                var initData = InitRegistry.Get(initDataId);
                int importedFuncCount = moduleInst.Repr.ImportedFunctions.Count;
                int localFuncCount = moduleInst.Repr.Funcs.Count;
                int totalFuncs = importedFuncCount + localFuncCount;
                // Bake per-type metadata (hashes + function-type flag) so
                // ref.test/ref.cast on funcref can resolve target types in
                // standalone mode. Indexed by declared type index.
                int typeCount = 0;
                while (moduleInst.Types.Contains((TypeIdx)typeCount)) typeCount++;
                if (typeCount > 0)
                {
                    initData.TypeHashes = new int[typeCount];
                    initData.TypeIsFunc = new bool[typeCount];
                    for (int t = 0; t < typeCount; t++)
                    {
                        var dt = moduleInst.Types[(TypeIdx)t];
                        initData.TypeHashes[t] = dt.GetHashCode();
                        initData.TypeIsFunc[t] = dt.Expansion is FunctionType;
                    }
                }

                if (totalFuncs > 0)
                {
                    initData.FuncTypeHashes = new int[totalFuncs];
                    initData.FuncTypeSuperHashes = new int[totalFuncs][];
                    int fi = 0;
                    foreach (var import in moduleInst.Repr.ImportedFunctions)
                    {
                        if (moduleInst.Types.Contains(import.TypeIndex))
                        {
                            var dt = moduleInst.Types[import.TypeIndex];
                            initData.FuncTypeHashes[fi] = dt.GetHashCode();
                            initData.FuncTypeSuperHashes[fi] = BuildSuperTypeHashChain(dt);
                        }
                        fi++;
                    }
                    foreach (var func in moduleInst.Repr.Funcs)
                    {
                        if (moduleInst.Types.Contains(func.TypeIndex))
                        {
                            var dt = moduleInst.Types[func.TypeIndex];
                            initData.FuncTypeHashes[fi] = dt.GetHashCode();
                            initData.FuncTypeSuperHashes[fi] = BuildSuperTypeHashChain(dt);
                        }
                        fi++;
                    }
                }
            }

            // Register emitted GC types for runtime initialization of GC globals
            foreach (var (typeIdx, gcType) in gcTypeEmitter.EmittedTypes)
            {
                if (gcType.ClrType != null)
                    GcTypeRegistry.Register(initDataId, typeIdx, gcType.ClrType);
            }

            // NOW we can generate the Module ctor IL (which will snapshot
            // the fully-populated initData) and finalize the types.
            moduleClassGen.Generate();
            var functionsType = typeBuilder.CreateType()!;
            var moduleClassType = moduleClassGen.CreateType();

            // Retrieve the actual MethodInfo objects from the baked type
            var methods = new MethodInfo[wasmFunctions.Count];
            var methodMap = new Dictionary<int, MethodInfo>();
            for (int i = 0; i < wasmFunctions.Count; i++)
            {
                methods[i] = functionsType.GetMethod(methodBuilders[i].Name)!;
                methodMap[i] = methods[i];
            }

            var result = new TranspilationResult(
                assemblyBuilder,
                moduleBuilder,
                functionsType,
                methods,
                manifest,
                methodMap,
                diagnostics.Diagnostics,
                interfaceGen.ExportsInterface?.UnderlyingSystemType,
                interfaceGen.ImportsInterface?.UnderlyingSystemType,
                moduleClassType,
                interfaceGen.ExportMethods,
                interfaceGen.ImportMethods,
                allFunctionTypes);
            // Stash post-bake re-evaluation work for element segments whose
            // initializers materialize GC objects (array.new / array.new_fixed
            // / array.new_default). Pre-bake those instantiations have no
            // runtime type to target; Bake replays them once the assembly
            // round-trips.
            result.SetPendingElemReeval(gcTypeEmitter, pendingElemReeval);
            result.InitDataId = moduleClassGen.InitDataId;
            return result;
        }

        private MethodBuilder CreateMethodStub(
            TypeBuilder typeBuilder,
            FunctionInstance funcInst,
            FunctionType funcType,
            int index)
        {
            // Build parameter types: ThinContext + wasm params + out params for multi-value
            var wasmParamTypes = funcType.ParameterTypes.Types;
            var resultTypes = funcType.ResultType.Types;
            int outParamCount = resultTypes.Length > 1 ? resultTypes.Length - 1 : 0;

            var paramTypes = new Type[1 + wasmParamTypes.Length + outParamCount];
            paramTypes[0] = typeof(ThinContext);
            for (int p = 0; p < wasmParamTypes.Length; p++)
            {
                paramTypes[p + 1] = MapValType(wasmParamTypes[p]);
            }
            // Out params for results 1..N (result 0 is the CLR return value)
            for (int r = 0; r < outParamCount; r++)
            {
                paramTypes[1 + wasmParamTypes.Length + r] = MapValType(resultTypes[r + 1]).MakeByRefType();
            }

            // Return type: result 0, or void if no results
            Type returnType = resultTypes.Length >= 1
                ? MapValType(resultTypes[0])
                : typeof(void);

            string methodName = !string.IsNullOrEmpty(funcInst.Name)
                ? $"Function_{funcInst.Name}"
                : $"Function{index}";

            var mb = typeBuilder.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes);

            // Name parameters for debuggability
            mb.DefineParameter(1, ParameterAttributes.None, "ctx");
            for (int p = 0; p < wasmParamTypes.Length; p++)
            {
                mb.DefineParameter(p + 2, ParameterAttributes.None, $"param{p}");
            }
            for (int r = 0; r < outParamCount; r++)
            {
                mb.DefineParameter(2 + wasmParamTypes.Length + r, ParameterAttributes.Out, $"result{r + 1}");
            }

            return mb;
        }

        /// <summary>
        /// Emit a fallback body that dispatches through the interpreter
        /// instead of throwing NotSupportedException. This allows transpiled
        /// functions to call non-transpiled siblings seamlessly.
        ///
        /// The fallback packs CLR-typed parameters into Value[], calls
        /// CallHelpers.InvokeFallback, and unpacks the result.
        /// </summary>
        private void EmitFallbackBody(MethodBuilder mb, int moduleFuncIndex, FunctionType funcType)
        {
            var il = mb.GetILGenerator();
            var paramTypes = funcType.ParameterTypes.Types;
            var resultTypes = funcType.ResultType.Types;

            // Build Value[] args from the CLR-typed parameters
            // arg 0 = ThinContext ctx, args 1..N = WASM params
            il.Emit(OpCodes.Ldc_I4, paramTypes.Length);
            il.Emit(OpCodes.Newarr, typeof(Value));

            for (int p = 0; p < paramTypes.Length; p++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, p);
                il.Emit(OpCodes.Ldarg, p + 1); // skip ctx at arg 0
                EmitBoxToValue(il, paramTypes[p]);
                il.Emit(OpCodes.Stelem, typeof(Value));
            }

            var argsLocal = il.DeclareLocal(typeof(Value[]));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Call InvokeFallback(ctx, funcIndex, args) → Value[]
            il.Emit(OpCodes.Ldarg_0); // ctx
            il.Emit(OpCodes.Ldc_I4, moduleFuncIndex);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Call, typeof(CallHelpers).GetMethod(
                nameof(CallHelpers.InvokeFallback),
                BindingFlags.Public | BindingFlags.Static)!);

            // Unpack result
            if (resultTypes.Length == 0)
            {
                il.Emit(OpCodes.Pop); // discard Value[]
            }
            else
            {
                // Result 0 is CLR return value
                var resultsLocal = il.DeclareLocal(typeof(Value[]));
                il.Emit(OpCodes.Stloc, resultsLocal);

                il.Emit(OpCodes.Ldloc, resultsLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem, typeof(Value));
                EmitUnboxFromValue(il, resultTypes[0]);

                // Results 1..N go to out params
                for (int r = 1; r < resultTypes.Length; r++)
                {
                    il.Emit(OpCodes.Ldarg, 1 + paramTypes.Length + (r - 1)); // out param
                    il.Emit(OpCodes.Ldloc, resultsLocal);
                    il.Emit(OpCodes.Ldc_I4, r);
                    il.Emit(OpCodes.Ldelem, typeof(Value));
                    EmitUnboxFromValue(il, resultTypes[r]);
                    EmitStind(il, resultTypes[r]);
                }
            }

            il.Emit(OpCodes.Ret);
        }

        /// <summary>
        /// Walk the supertype chain of <paramref name="dt"/> (depth-first,
        /// self first) and collect the StableHash of every transitive
        /// supertype. Used by <c>ref.test</c>/<c>ref.cast</c> on funcref
        /// to decide subtype relationships at runtime when the interpreter
        /// TypesSpace isn't available (doc 1 §11.8).
        /// </summary>
        private static int[] BuildSuperTypeHashChain(Wacs.Core.Types.DefType dt)
        {
            var chain = new List<int> { dt.GetHashCode() };
            var seen = new HashSet<int> { dt.GetHashCode() };
            var worklist = new Stack<Wacs.Core.Types.DefType>();
            if (dt.SuperTypes != null)
                foreach (var sup in dt.SuperTypes) worklist.Push(sup);
            while (worklist.Count > 0)
            {
                var s = worklist.Pop();
                if (s == null) continue;
                int h = s.GetHashCode();
                if (!seen.Add(h)) continue;
                chain.Add(h);
                if (s.SuperTypes != null)
                    foreach (var ss in s.SuperTypes) worklist.Push(ss);
            }
            return chain.ToArray();
        }

        /// <summary>
        /// Evaluate a const expression from an element segment initializer.
        /// Handles the subset of const expressions that the spec allows here:
        /// i{32,64}.const, f{32,64}.const, ref.null, ref.func, ref.i31,
        /// array.new, array.new_default, array.new_fixed. GC constructors
        /// produce a Value whose GcRef is a GcObjectAdapter wrapping an
        /// instance of the emitted CLR type, so <see cref="Emitters.GcRuntimeHelpers.ExtractElemValue"/>
        /// can unwrap it at runtime without re-running the expression.
        /// </summary>
        /// <summary>
        /// Bake-time entry point for re-evaluating element-segment
        /// initializers once the assembly has been saved+loaded.
        /// <see cref="GcTypeEmitter.RemapEmittedToLoadedTypes"/> must
        /// have been called first so <c>BuildArrayInstance</c>'s
        /// <c>Activator.CreateInstance</c> sees runtime types.
        /// </summary>
        internal static Value EvaluateElemExprForBake(
            Wacs.Core.Types.Expression expr,
            GcTypeEmitter gcTypeEmitter)
            => EvaluateElemExpr(expr, gcTypeEmitter);

        /// <summary>
        /// Whether <paramref name="expr"/> contains any GC constructor
        /// (<c>array.new</c>, <c>array.new_default</c>, <c>array.new_fixed</c>)
        /// that needs the emitted CLR type to be runtime-instantiable.
        /// </summary>
        private static bool HasGcConstructor(Wacs.Core.Types.Expression expr)
        {
            foreach (var inst in expr.Instructions)
            {
                if (inst is Wacs.Core.Instructions.GC.InstArrayNew
                    || inst is Wacs.Core.Instructions.GC.InstArrayNewDefault
                    || inst is Wacs.Core.Instructions.GC.InstArrayNewFixed)
                    return true;
            }
            return false;
        }

        private static Value EvaluateElemExpr(
            Wacs.Core.Types.Expression expr,
            GcTypeEmitter? gcTypeEmitter)
        {
            var stack = new System.Collections.Generic.Stack<Value>();
            foreach (var inst in expr.Instructions)
            {
                switch (inst)
                {
                    case Wacs.Core.Instructions.Numeric.InstI32Const ic:
                        stack.Push(new Value(ic.Value));
                        continue;
                    case Wacs.Core.Instructions.Numeric.InstI64Const lc:
                        stack.Push(new Value(lc.FetchImmediate(null!)));
                        continue;
                    case Wacs.Core.Instructions.Numeric.InstF32Const fc:
                        stack.Push(new Value(fc.FetchImmediate(null!)));
                        continue;
                    case Wacs.Core.Instructions.Numeric.InstF64Const dc:
                        stack.Push(new Value(dc.FetchImmediate(null!)));
                        continue;
                    case Wacs.Core.Instructions.Reference.InstRefFunc rf:
                        stack.Push(new Value(ValType.FuncRef, (int)rf.FunctionIndex.Value));
                        continue;
                    case Wacs.Core.Instructions.Reference.InstRefNull rn:
                        stack.Push(new Value(rn.RefType));
                        continue;
                    case Wacs.Core.Instructions.GC.InstRefI31:
                    {
                        int v = (int)stack.Pop().Data.Int32;
                        stack.Push(GcRuntimeHelpers.RefI31Value(v));
                        continue;
                    }
                    case Wacs.Core.Instructions.GC.InstArrayNew an:
                    {
                        int length = stack.Pop().Data.Int32;
                        var initVal = stack.Pop();
                        stack.Push(BuildArrayInstance(gcTypeEmitter, an.TypeIndex, length, initVal, fillAll: true));
                        continue;
                    }
                    case Wacs.Core.Instructions.GC.InstArrayNewDefault adef:
                    {
                        int length = stack.Pop().Data.Int32;
                        stack.Push(BuildArrayInstance(gcTypeEmitter, adef.TypeIndex, length, default, fillAll: false));
                        continue;
                    }
                    case Wacs.Core.Instructions.GC.InstArrayNewFixed afix:
                    {
                        int n = afix.FixedCount;
                        var elems = new Value[n];
                        for (int k = n - 1; k >= 0; k--) elems[k] = stack.Pop();
                        stack.Push(BuildArrayInstanceFixed(gcTypeEmitter, afix.TypeIndex, elems));
                        continue;
                    }
                }
            }
            // Fall through: return the top of the stack if present. If the
            // expression was entirely unhandled, yield a null any-ref so
            // downstream reads don't produce malformed Values.
            return stack.Count > 0 ? stack.Pop() : new Value(ValType.Any);
        }

        /// <summary>
        /// Instantiate the emitted CLR array class for <paramref name="typeIdx"/>
        /// and populate with scalar / ref elements. <paramref name="fillAll"/> picks
        /// between array.new (fill with initVal) and array.new_default (leave zero).
        /// </summary>
        private static Value BuildArrayInstance(
            GcTypeEmitter? gcTypeEmitter, int typeIdx, int length, Value initVal, bool fillAll)
        {
            if (gcTypeEmitter == null) return new Value(ValType.Any);
            var clrType = gcTypeEmitter.GetEmittedType(typeIdx);
            if (clrType == null) return new Value(ValType.Any);

            // Under PersistedAssemblyBuilder the emitted class is metadata-
            // only at this point — Activator.CreateInstance throws
            // "Type must be a type provided by the runtime." Element-segment
            // array.new evaluation needs a deferred-instantiation rewire
            // (tracked as follow-up); for now, fall back to a typed-null
            // Value so transpile completes.
            object? instance;
            try
            {
                instance = Activator.CreateInstance(clrType);
            }
            catch (ArgumentException)
            {
                return new Value(ValType.Any);
            }
            var elemField = clrType.GetField("elements");
            var lenField = clrType.GetField("length");
            if (instance == null || elemField == null) return new Value(ValType.Any);

            var elemType = elemField.FieldType.GetElementType()!;
            var arr = Array.CreateInstance(elemType, length);
            if (fillAll)
            {
                for (int i = 0; i < length; i++)
                    arr.SetValue(ElementFromValue(initVal, elemType), i);
            }
            elemField.SetValue(instance, arr);
            lenField?.SetValue(instance, length);
            return new Value((ValType)typeIdx | ValType.Ref, 0, new Emitters.GcRuntimeHelpers.GcObjectAdapter(instance));
        }

        private static Value BuildArrayInstanceFixed(
            GcTypeEmitter? gcTypeEmitter, int typeIdx, Value[] elems)
        {
            if (gcTypeEmitter == null) return new Value(ValType.Any);
            var clrType = gcTypeEmitter.GetEmittedType(typeIdx);
            if (clrType == null) return new Value(ValType.Any);

            // See BuildArrayInstance for the metadata-only fallback rationale.
            object? instance;
            try
            {
                instance = Activator.CreateInstance(clrType);
            }
            catch (ArgumentException)
            {
                return new Value(ValType.Any);
            }
            var elemField = clrType.GetField("elements");
            var lenField = clrType.GetField("length");
            if (instance == null || elemField == null) return new Value(ValType.Any);

            var elemType = elemField.FieldType.GetElementType()!;
            var arr = Array.CreateInstance(elemType, elems.Length);
            for (int i = 0; i < elems.Length; i++)
                arr.SetValue(ElementFromValue(elems[i], elemType), i);
            elemField.SetValue(instance, arr);
            lenField?.SetValue(instance, elems.Length);
            return new Value((ValType)typeIdx | ValType.Ref, 0, new Emitters.GcRuntimeHelpers.GcObjectAdapter(instance));
        }

        /// <summary>Convert a Value (from the const-eval stack) to the target array
        /// element CLR type. Mirrors the inlined conversions in the runtime helper.</summary>
        private static object? ElementFromValue(Value val, Type elemType)
        {
            if (elemType == typeof(byte) || elemType == typeof(sbyte))
                return Convert.ChangeType(val.Data.Int32 & 0xFF, elemType);
            if (elemType == typeof(short) || elemType == typeof(ushort))
                return Convert.ChangeType(val.Data.Int32 & 0xFFFF, elemType);
            if (elemType == typeof(int)) return val.Data.Int32;
            if (elemType == typeof(long)) return val.Data.Int64;
            if (elemType == typeof(float)) return val.Data.Float32;
            if (elemType == typeof(double)) return val.Data.Float64;
            if (val.GcRef != null)
                return val.GcRef is Emitters.GcRuntimeHelpers.GcObjectAdapter a ? a.Target : val.GcRef;
            return null;
        }

        private static void EmitBoxToValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(int) })!);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(long) })!);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(float) })!);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Newobj, typeof(Value).GetConstructor(new[] { typeof(double) })!);
                    break;
                default:
                    break; // Reference types are already Value on the CIL stack
            }
        }

        private static readonly FieldInfo DataField =
            typeof(Value).GetField(nameof(Value.Data))!;
        private static readonly FieldInfo Int32Field =
            typeof(DUnion).GetField(nameof(DUnion.Int32))!;
        private static readonly FieldInfo Int64Field =
            typeof(DUnion).GetField(nameof(DUnion.Int64))!;
        private static readonly FieldInfo Float32Field =
            typeof(DUnion).GetField(nameof(DUnion.Float32))!;
        private static readonly FieldInfo Float64Field =
            typeof(DUnion).GetField(nameof(DUnion.Float64))!;

        private static void EmitUnboxFromValue(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32:
                case ValType.I64:
                case ValType.F32:
                case ValType.F64:
                {
                    var local = il.DeclareLocal(typeof(Value));
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);
                    il.Emit(OpCodes.Ldflda, DataField);
                    il.Emit(OpCodes.Ldfld, type switch
                    {
                        ValType.I32 => Int32Field,
                        ValType.I64 => Int64Field,
                        ValType.F32 => Float32Field,
                        ValType.F64 => Float64Field,
                        _ => throw new InvalidOperationException()
                    });
                    break;
                }
                default:
                    break; // Reference types stay as Value
            }
        }

        private static void EmitStind(ILGenerator il, ValType type)
        {
            switch (type)
            {
                case ValType.I32: il.Emit(OpCodes.Stind_I4); break;
                case ValType.I64: il.Emit(OpCodes.Stind_I8); break;
                case ValType.F32: il.Emit(OpCodes.Stind_R4); break;
                case ValType.F64: il.Emit(OpCodes.Stind_R8); break;
                default: il.Emit(OpCodes.Stobj, typeof(Value)); break;
            }
        }

        /// <summary>
        /// Maps a WebAssembly value type to the corresponding CLR type for use
        /// in transpiled method signatures.
        /// </summary>
        internal static Type MapValType(ValType type)
        {
            return type switch
            {
                ValType.I32 => typeof(int),
                ValType.I64 => typeof(long),
                ValType.F32 => typeof(float),
                ValType.F64 => typeof(double),
                // Reference types and V128 stay as Value on the CLR stack
                _ => typeof(Value)
            };
        }

        /// <summary>
        /// Maps a WASM value type to its internal CIL representation.
        /// GC ref types (struct, array, i31, eq, any, none) use typeof(object)
        /// internally on the CIL stack and in locals, avoiding Value boxing overhead.
        /// Funcref/externref stay as Value since they use Data.Ptr (not GcRef).
        /// Function signatures always use MapValType (Value at boundaries).
        /// Concrete type indices (defType) are disambiguated via
        /// <paramref name="moduleInst"/> — function types resolve to funcref
        /// representation; struct/array types resolve to GC ref.
        /// </summary>
        internal static Type MapValTypeInternal(ValType type, ModuleInstance? moduleInst = null)
        {
            if (IsExnRefType(type)) return typeof(WasmException);
            if (IsGcRefType(type, moduleInst)) return typeof(object);
            return MapValType(type);
        }

        /// <summary>
        /// Returns true for exnref types — the internal CIL representation
        /// is the CLR WasmException reference itself (doc 1 §2.1, §13).
        /// </summary>
        internal static bool IsExnRefType(ValType type)
            => type == ValType.Exn || type == ValType.NoExn;

        /// <summary>
        /// Returns true for GC reference types that should use typeof(object)
        /// on the internal CIL evaluation stack (doc 2 §1 invariant 1). These
        /// types store their data as a managed CLR reference, not as Value.Data.Ptr.
        /// Funcref/externref and concrete function types are NOT included —
        /// they stay as Value (doc 2 §1 invariant 3).
        /// </summary>
        internal static bool IsGcRefType(ValType type, ModuleInstance? moduleInst = null)
        {
            if (!type.IsRefType()) return false;
            // Exclude funcref, externref, exnref, and their bottom types —
            // those have their own representations (doc 2 §1 invariants 3 & 4).
            switch (type)
            {
                case ValType.FuncRef:
                case ValType.Func:
                case ValType.NoFunc:
                case ValType.NoFuncNN:
                case ValType.ExternRef:
                case ValType.Extern:
                case ValType.NoExtern:
                case ValType.NoExternNN:
                case ValType.Exn:
                case ValType.NoExn:
                    return false;
            }
            // Concrete type index (defType): disambiguate by expansion kind.
            // Function types flow as Value (funcref encoding); struct/array
            // types flow as object.
            if (type.IsDefType() && moduleInst?.Types != null)
            {
                var idx = type.Index();
                if (moduleInst.Types.Contains(idx))
                {
                    var expansion = moduleInst.Types[idx].Expansion;
                    if (expansion is FunctionType) return false;
                }
            }
            // any, eq, i31, struct, array, none, and (without module) assume
            // GC ref for safety — callers with access to module context should
            // always pass it to disambiguate concrete types.
            return true;
        }
    }
}
