// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Wacs.ComponentModel.Async.SourceGen
{
    /// <summary>
    /// Scans the current compilation for partial classes marked
    /// <c>[AsyncComponentHarness]</c> and emits the missing
    /// constructor + the implementation of each
    /// <c>[AsyncExport("export-name")]</c>-marked partial method.
    /// Wires every emitted method through
    /// <c>ComponentInstance.InstantiateAot</c> +
    /// <c>InvokeCoreAsyncLift</c> so the consumer's code stays
    /// on the AOT-safe primitive surface (never reaches the
    /// reflective typed-bridge in <c>ComponentInstance.Invoke</c>).
    ///
    /// <para>Companion to
    /// <see cref="CanonOpRegistryGenerator"/> /
    /// <see cref="ComponentLifterRegistryGenerator"/>: each
    /// trigger is independent. This generator runs in every
    /// consumer assembly that declares an
    /// <c>[AsyncComponentHarness]</c> class.</para>
    ///
    /// <para><b>Spec:</b> wit-component's lift naming convention
    /// has stabilized on <c>[async-lift]&lt;qualified-export&gt;</c>
    /// for async-lifted exports; sync exports use the plain export
    /// name. The <c>[AsyncExport]</c> string is opaque to the
    /// generator — we just emit it as the
    /// <c>InvokeCoreAsyncLift</c> argument.</para>
    /// </summary>
    [Generator]
    public sealed class AsyncComponentHarnessGenerator
        : IIncrementalGenerator
    {
        private const string HarnessAttributeFqn =
            "Wacs.ComponentModel.Async.AsyncComponentHarnessAttribute";
        private const string ExportAttributeFqn =
            "Wacs.ComponentModel.Async.AsyncExportAttribute";
        private const string SyncExportAttributeFqn =
            "Wacs.ComponentModel.Async.SyncExportAttribute";
        private const string WitRecordAttributeFqn =
            "Wacs.ComponentModel.Async.WitRecordAttribute";
        private const string ComponentInstanceFqn =
            "Wacs.ComponentModel.Runtime.ComponentInstance";

        // Diagnostic descriptors. Surface generator-level
        // misuse to the consumer at build time with clear,
        // actionable messages — the alternative (the generator
        // silently skipping or emitting code that fails to
        // compile downstream) is hostile to users debugging
        // attribute placement.
        private static readonly DiagnosticDescriptor
            HarnessClassMustBePartial = new(
                id: "WACSCM001",
                title: "AsyncComponentHarness class must be partial",
                messageFormat:
                    "[AsyncComponentHarness] is applied to " +
                    "'{0}' but the class isn't declared partial. " +
                    "Add the 'partial' keyword so the generator " +
                    "can emit the constructor + InvokeCoreAsyncLift " +
                    "wiring as a partial-class body.",
                category: "Wacs.ComponentModel.Async.SourceGen",
                defaultSeverity: DiagnosticSeverity.Error,
                isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor
            ExportMethodMustBePartial = new(
                id: "WACSCM002",
                title: "AsyncExport method must be a partial definition",
                messageFormat:
                    "[AsyncExport] is applied to '{0}' on '{1}' " +
                    "but the method isn't a partial definition. " +
                    "Declare it as `partial` so the generator can " +
                    "emit the body.",
                category: "Wacs.ComponentModel.Async.SourceGen",
                defaultSeverity: DiagnosticSeverity.Error,
                isEnabledByDefault: true);

        public void Initialize(
            IncrementalGeneratorInitializationContext context)
        {
            var scanResults = context.CompilationProvider.Select(
                static (compilation, _) =>
                {
                    var attr = compilation.GetTypeByMetadataName(
                        HarnessAttributeFqn);
                    if (attr == null)
                        return default(ScanResult);
                    return CollectAndDiagnose(compilation);
                });

            context.RegisterSourceOutput(scanResults,
                static (spc, scan) =>
                {
                    if (scan.IsDefault) return;
                    foreach (var diag in scan.Diagnostics)
                        spc.ReportDiagnostic(diag);
                    foreach (var cls in scan.Classes)
                        spc.AddSource(
                            cls.GeneratedFileName,
                            EmitHarness(cls));
                });
        }

        private readonly struct ScanResult
        {
            public ImmutableArray<HarnessClass> Classes { get; }
            public ImmutableArray<Diagnostic> Diagnostics { get; }
            // Record layouts discovered from [WitRecord]-
            // marked types in the compilation. Keyed by the
            // record's fully-qualified type name (the same
            // string that shows up in parameter types).
            public System.Collections.Immutable.ImmutableDictionary<
                string, RecordLayout> Records { get; }
            public bool IsDefault =>
                Classes.IsDefault && Diagnostics.IsDefault;
            public ScanResult(
                ImmutableArray<HarnessClass> classes,
                ImmutableArray<Diagnostic> diagnostics,
                System.Collections.Immutable.ImmutableDictionary<
                    string, RecordLayout> records)
            {
                Classes = classes;
                Diagnostics = diagnostics;
                Records = records;
            }
        }

        // Layout of a [WitRecord]-marked type. Field order
        // follows declaration; offsets are canon-ABI primitive
        // layout — `align_to(prev_end, alignof(field))`.
        private readonly struct RecordLayout
        {
            public string TypeFqn { get; }
            public System.Collections.Immutable.ImmutableArray<
                RecordField> Fields { get; }
            public RecordLayout(string typeFqn,
                System.Collections.Immutable.ImmutableArray<
                    RecordField> fields)
            {
                TypeFqn = typeFqn;
                Fields = fields;
            }
        }

        private readonly struct RecordField
        {
            public string Name { get; }
            public string Type { get; }
            public int Offset { get; }
            public RecordField(string name, string type, int offset)
            {
                Name = name; Type = type; Offset = offset;
            }
        }

        private readonly struct HarnessClass
        {
            public string Namespace { get; }
            public string ClassName { get; }
            public string Accessibility { get; }
            public ImmutableArray<ExportMethod> Exports { get; }
            public System.Collections.Immutable
                .ImmutableDictionary<string, RecordLayout> Records { get; }
            public string GeneratedFileName { get; }
            public HarnessClass(
                string ns, string className, string accessibility,
                ImmutableArray<ExportMethod> exports,
                System.Collections.Immutable.ImmutableDictionary<
                    string, RecordLayout> records)
            {
                Namespace = ns;
                ClassName = className;
                Accessibility = accessibility;
                Exports = exports;
                Records = records;
                GeneratedFileName =
                    (string.IsNullOrEmpty(ns) ? "" : ns + ".")
                    + className + ".Harness.g.cs";
            }
        }

        private enum ExportKind { Async, Sync }

        private readonly struct ExportMethod
        {
            public string MethodName { get; }
            public string ExportName { get; }
            public string Accessibility { get; }
            public ImmutableArray<ExportParam> Parameters { get; }
            public string? ReturnType { get; }
            public ExportKind Kind { get; }
            public ExportMethod(
                string methodName, string exportName,
                string accessibility,
                ImmutableArray<ExportParam> parameters,
                string? returnType,
                ExportKind kind)
            {
                MethodName = methodName;
                ExportName = exportName;
                Accessibility = accessibility;
                Parameters = parameters;
                ReturnType = returnType;
                Kind = kind;
            }
        }

        private readonly struct ExportParam
        {
            public string Name { get; }
            public string Type { get; }
            public ExportParam(string name, string type)
            {
                Name = name; Type = type;
            }
        }

        // Canon-ABI primitive types — pass straight through
        // the canon-async lift adapter (boxed for object[]
        // calls; statically-known for sync CreateInvokerFunc).
        private static readonly HashSet<string> PrimitiveTypes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "int", "uint", "long", "ulong",
                "byte", "sbyte", "short", "ushort",
                "bool", "float", "double",
                "System.Int32", "System.UInt32",
                "System.Int64", "System.UInt64",
                "System.Byte", "System.SByte",
                "System.Int16", "System.UInt16",
                "System.Boolean", "System.Single",
                "System.Double",
            };

        // Aggregate types we know how to marshal at the
        // generated-code layer via Wacs.ComponentModel.Harness
        // helpers (StringCoding + MemoryHelpers) and inline
        // byte-copy code.
        private const string StringType = "string";
        private const string StringTypeFq = "System.String";
        private const string ByteArrayType = "byte[]";
        private const string ByteArrayTypeFq = "System.Byte[]";

        private static bool IsPrimitive(string fqType) =>
            PrimitiveTypes.Contains(fqType);

        private static bool IsString(string fqType) =>
            fqType == StringType || fqType == StringTypeFq;

        private static bool IsByteArray(string fqType) =>
            fqType == ByteArrayType || fqType == ByteArrayTypeFq;

        // Primitive arrays representing canon-ABI list<T>.
        // Memory layout: contiguous `N * sizeof(T)` bytes,
        // aligned to alignof(T). Bulk copy via
        // Buffer.BlockCopy with scaled byte count.
        private static readonly System.Collections.Generic
            .HashSet<string> PrimitiveArrayTypes =
            new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal)
            {
                "byte[]", "sbyte[]", "ushort[]", "short[]",
                "uint[]", "int[]", "ulong[]", "long[]",
                "float[]", "double[]", "bool[]",
                "System.Byte[]", "System.SByte[]",
                "System.UInt16[]", "System.Int16[]",
                "System.UInt32[]", "System.Int32[]",
                "System.UInt64[]", "System.Int64[]",
                "System.Single[]", "System.Double[]",
                "System.Boolean[]",
            };

        private static bool IsPrimitiveArray(string fqType) =>
            PrimitiveArrayTypes.Contains(fqType);

        // Strip the trailing `[]` to get the element type
        // for sizeof/alignof and the C# generic-arg form.
        private static string ArrayElementType(string fqType)
            => fqType.EndsWith("[]",
                    StringComparison.Ordinal)
                ? fqType.Substring(0, fqType.Length - 2)
                : fqType;

        // String + primitive arrays + simple lists all
        // flatten to (i32 ptr, i32 len) and can ride a
        // single-expression / single-helper lift path. Treat
        // them uniformly in size / align / slot / flat-sig
        // contexts. The lift / lower dispatch at codegen
        // sites still branches on the specific element-type
        // family (IsString, IsByteArray, IsPrimitiveArray,
        // IsListType) to pick the right helper.
        //
        // list<record> stays out of this predicate — its lift
        // is per-record-type and can't be expressed as a
        // single helper call.
        private static bool IsPtrLenAggregate(string fqType) =>
            IsString(fqType)
            || IsPrimitiveArray(fqType)
            || IsSimpleList(fqType);

        // Canon-ABI list<T> where T's lift is multi-statement
        // — list<string> (`string[]`) and list<record>
        // (`R[]` for a [WitRecord] R whose every field is
        // primitive or string). Same flat (ptr, len) handle
        // as IsPtrLenAggregate shapes; codegen branches on
        // the element type and per-field.
        private static bool IsListType(string fqType)
        {
            if (!fqType.EndsWith("[]",
                    StringComparison.Ordinal))
                return false;
            if (IsPrimitiveArray(fqType)) return false;
            string elem = ArrayElementType(fqType);
            if (IsString(elem)) return true;
            // list-of-list: outer flat shape is still (ptr,
            // len). The inner list is itself a ptr/len shape
            // — at any nesting depth.
            if (IsPrimitiveArray(elem)) return true;
            if (IsListType(elem)) return true;
            if (TryLookupRecord(elem, out var rec))
                return IsRecordSupportedAsListElement(rec);
            return false;
        }

        // A "simple list" is a list whose lift and lower can
        // ride single-expression / single-helper paths —
        // `string[]` and `T[][]` for primitive T. Excludes
        // list<record> (each record needs per-field
        // codegen) and deeper nestings (e.g.,
        // list<list<list<X>>>) which would need their own
        // helper. Used to gate list-shape acceptance in
        // option / result arm contexts.
        private static bool IsSimpleList(string fqType)
        {
            if (!IsListType(fqType)) return false;
            string elem = ArrayElementType(fqType);
            return IsString(elem)
                || IsPrimitiveArray(elem);
        }

        // True iff every field of `rec` is canon-ABI
        // primitive, ptr/len aggregate (string / byte[] /
        // primitive array / simple list — via the widened
        // `IsPtrLenAggregate`), option of either primitive
        // or ptr/len. Result / nested-record / nested-list
        // fields still land later.
        private static bool IsRecordSupportedAsListElement(
            RecordLayout rec)
        {
            foreach (var f in rec.Fields)
            {
                if (IsPrimitive(f.Type)) continue;
                if (IsPtrLenAggregate(f.Type)) continue;
                if (IsNullablePrimitive(f.Type)) continue;
                if (IsOptionRef(f.Type)) continue;
                // Nested record field: the inner record must
                // itself be supported as a list element so
                // the per-element lower / lift can recurse.
                if (TryLookupRecord(f.Type, out var sub)
                    && IsRecordSupportedAsListElement(sub))
                    continue;
                // result<...> field — every shape already
                // gated by IsSupportedResult is compatible
                // with the inline lift / lower since result
                // payloads are at most a (disc, ptr/len) or
                // (disc, slot1, slot2) tuple of memory reads.
                if (IsWitResult(f.Type)
                    && IsSupportedResult(f.Type))
                    continue;
                return false;
            }
            return true;
        }

        // Canon-ABI option<T> for primitive T. C# representation
        // is Nullable<T> — `int?`, `bool?`, `long?`, etc.
        // Flat lowering: (i32 disc, payloadSlot...). For
        // primitive T the payload is a single slot matching T's
        // canon-ABI slot kind (i32 for i8/i16/i32, i64 for i64,
        // f32/f64 for floats).
        private static readonly HashSet<string> NullablePrimitiveTypes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "int?", "uint?", "long?", "ulong?",
                "byte?", "sbyte?", "short?", "ushort?",
                "bool?", "float?", "double?",
                "System.Int32?", "System.UInt32?",
                "System.Int64?", "System.UInt64?",
                "System.Byte?", "System.SByte?",
                "System.Int16?", "System.UInt16?",
                "System.Boolean?", "System.Single?",
                "System.Double?",
            };

        private static bool IsNullablePrimitive(string fqType) =>
            NullablePrimitiveTypes.Contains(fqType);

        // option<T> where the inner T is a ptr/len aggregate
        // (string / byte[] / primitive array) or a simple
        // list (`string[]`, `T[][]` for primitive T).
        // Detection relies on the nullable-annotation `?`
        // having been appended by TypeDisplayWithNullability
        // during the parse pass — C#'s FullyQualifiedFormat
        // does NOT include the `?` for reference-type
        // nullables on its own.
        private static bool IsOptionRef(string fqType)
        {
            if (!fqType.EndsWith("?", StringComparison.Ordinal))
                return false;
            string inner = InnerOfNullable(fqType);
            return IsPtrLenAggregate(inner)
                || IsSimpleList(inner);
        }

        // Either flavor of canon-ABI option<T>:
        // primitive (T?) or ptr/len (string? / byte[]?).
        private static bool IsOption(string fqType)
            => IsNullablePrimitive(fqType)
                || IsOptionRef(fqType);

        // Canon-ABI result<TOk, TErr> — variant with two arms.
        // C# rep: Wacs.ComponentModel.Harness.WitResult<TOk, TErr>.
        // Either arm can be System.ValueTuple (the elided
        // unit case) for `result`/`result<T>`/`result<_, E>`
        // WIT shapes. Both arms must be primitive or unit
        // for the MVP — aggregate-in-result lands in the
        // next pass.
        private const string WitResultPrefixUnglobal =
            "Wacs.ComponentModel.Harness.WitResult<";
        private const string WitResultPrefixGlobal =
            "global::Wacs.ComponentModel.Harness.WitResult<";
        private const string ValueTupleType =
            "System.ValueTuple";
        private const string ValueTupleTypeGlobal =
            "global::System.ValueTuple";

        // Roslyn's FullyQualifiedFormat prepends `global::` to
        // every namespace-qualified name. Accept both.
        private static bool IsWitResult(string fqType)
        {
            if (!fqType.EndsWith(">",
                StringComparison.Ordinal))
                return false;
            return fqType.StartsWith(WitResultPrefixUnglobal,
                    StringComparison.Ordinal)
                || fqType.StartsWith(WitResultPrefixGlobal,
                    StringComparison.Ordinal);
        }

        // Display a type name with nullable-reference
        // annotations preserved. Roslyn's FullyQualifiedFormat
        // includes `?` for Nullable<T> value types but NOT for
        // nullable reference types — for those it just shows
        // the underlying type. We need both forms to be
        // distinguishable for canon-ABI option<T>.
        private static string TypeDisplayWithNullability(
            ITypeSymbol type)
        {
            string s = type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat);
            if (type.NullableAnnotation
                    == NullableAnnotation.Annotated
                && type.IsReferenceType
                && !s.EndsWith("?",
                    StringComparison.Ordinal))
            {
                s += "?";
            }
            return s;
        }

        // Strip the `global::` prefix the Roslyn FullyQualifiedFormat
        // emits, so the rest of the codegen can pattern-match
        // primitive names without two-form awareness.
        private static string StripGlobal(string fqType) =>
            fqType.StartsWith("global::",
                StringComparison.Ordinal)
                ? fqType.Substring("global::".Length)
                : fqType;

        // Extract the (TOk, TErr) type-arg pair from a
        // WitResult<TOk, TErr> string. Handles nested
        // generics correctly via angle-bracket depth
        // tracking so `WitResult<List<int>, string>` parses
        // as `(List<int>, string)`.
        private static (string Ok, string Err)
            WitResultArgs(string fqType)
        {
            int start = fqType.StartsWith(WitResultPrefixGlobal,
                StringComparison.Ordinal)
                ? WitResultPrefixGlobal.Length
                : WitResultPrefixUnglobal.Length;
            int end = fqType.Length - 1;
            int depth = 0;
            int comma = -1;
            for (int i = start; i < end; i++)
            {
                char c = fqType[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    comma = i;
                    break;
                }
            }
            if (comma < 0) return (fqType, "");
            // Normalize the `global::` prefix off the type
            // args so downstream switch tables can match
            // unqualified primitive names.
            string ok = StripGlobal(fqType.Substring(
                start, comma - start).Trim());
            string err = StripGlobal(fqType.Substring(
                comma + 1, end - comma - 1).Trim());
            return (ok, err);
        }

        // Result arms supported by the MVP: primitives plus
        // the elided-unit System.ValueTuple sentinel.
        private static bool IsResultArm(string fqType) =>
            IsPrimitive(fqType)
            || IsPtrLenAggregate(fqType)
            || IsSimpleList(fqType)
            || fqType == ValueTupleType;

        private static bool IsSupportedResult(string fqType)
        {
            if (!IsWitResult(fqType)) return false;
            var (ok, err) = WitResultArgs(fqType);
            return IsResultArm(ok) && IsResultArm(err)
                && ResultJoinedPayload(ok, err) != "MIXED";
        }

        // Canon-ABI tuple<T1, T2, ...> — C# represents it as
        // either `(T1, T2)` (anonymous) or `(T1 name1, T2 name2)`
        // (named). Roslyn's FullyQualifiedFormat uses these
        // syntaxes verbatim. The MVP supports tuples whose
        // every element is a primitive; aggregate-in-tuple
        // (string / option / nested tuple) is the next slice.
        private static bool IsTuple(string fqType)
        {
            if (fqType.Length < 5) return false;
            if (fqType[0] != '(') return false;
            if (fqType[fqType.Length - 1] != ')') return false;
            // Must have at least one top-level comma to
            // qualify as a tuple rather than a parenthesized
            // expression.
            int depth = 0;
            for (int i = 1; i < fqType.Length - 1; i++)
            {
                char c = fqType[i];
                if (c == '(' || c == '<') depth++;
                else if (c == ')' || c == '>') depth--;
                else if (c == ',' && depth == 0) return true;
            }
            return false;
        }

        // Parse a Roslyn-formatted tuple type into its element
        // types. Strips any trailing element-name tokens.
        // Doesn't normalize `global::` prefixes — caller
        // applies StripGlobal where it matters.
        private static System.Collections.Generic.List<string>
            TupleElementTypes(string fqType)
        {
            var result = new System.Collections.Generic.List<string>();
            int start = 1;
            int depth = 0;
            for (int i = 1; i < fqType.Length; i++)
            {
                char c = fqType[i];
                if (c == '(' || c == '<') depth++;
                else if (c == ')' || c == '>')
                {
                    depth--;
                    if (depth < 0)
                    {
                        AddElement(result, fqType, start, i);
                        break;
                    }
                }
                else if (c == ',' && depth == 0)
                {
                    AddElement(result, fqType, start, i);
                    start = i + 1;
                }
            }
            return result;
        }

        private static void AddElement(
            System.Collections.Generic.List<string> result,
            string source, int start, int end)
        {
            string element = source.Substring(start, end - start)
                .Trim();
            // Drop a trailing identifier (the named-tuple
            // element name) if present. Identifier starts
            // after the last whitespace.
            int lastSpace = element.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                // Heuristic: the part after the last space is
                // the element name iff it doesn't contain any
                // type-construction chars (<, >, comma, [).
                string tail = element.Substring(lastSpace + 1);
                bool tailIsIdent = tail.Length > 0;
                foreach (var c in tail)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_'))
                    {
                        tailIsIdent = false;
                        break;
                    }
                }
                if (tailIsIdent)
                    element = element.Substring(0, lastSpace).Trim();
            }
            result.Add(StripGlobal(element));
        }

        // Tuple of primitives and / or ptr/len aggregates
        // (string, byte[]). Nested tuples, options, results
        // and records inside tuples are still out of scope —
        // the per-element walking treats fields uniformly via
        // FieldSize / FieldAlign, but the lower/lift codegen
        // only knows how to emit primitive + string + byte[]
        // glue.
        private static bool IsSupportedTuple(string fqType)
        {
            if (!IsTuple(fqType)) return false;
            var elems = TupleElementTypes(fqType);
            foreach (var e in elems)
                if (!IsSupportedFieldType(e))
                    return false;
            return elems.Count > 0;
        }

        // True for record/tuple field types that the
        // BuildRecordLayout + flat-codegen paths handle.
        // Records nest recursively — a record field's own
        // field types are validated when its layout is built.
        private static bool IsSupportedFieldType(string fqType)
            => IsPrimitive(fqType)
                || IsPtrLenAggregate(fqType)
                || IsListType(fqType)
                || IsOption(fqType)
                || IsSupportedResult(fqType)
                || IsRecord(fqType);

        // Per-emission record-layout lookup. Set at the start
        // of each EmitHarness call, cleared after. ThreadStatic
        // because Roslyn generators may run concurrently across
        // compilations.
        [System.ThreadStatic]
        private static System.Collections.Generic
            .IReadOnlyDictionary<string, RecordLayout>?
            _activeRecords;

        // Counter for unique inner-list base names within a
        // single emit pass. Per `EmitHarness` call we reset
        // to 0; each EmitPtrLenAssignInto-of-list bump gives
        // a fresh suffix so multiple lowerings in the same
        // outer scope can't collide on the inner-list locals.
        [System.ThreadStatic]
        private static int _listInnerCounter;

        private static int NextListInnerId()
            => ++_listInnerCounter;

        private static bool TryLookupRecord(
            string fqType, out RecordLayout layout)
        {
            var key = StripGlobal(fqType);
            if (_activeRecords != null
                && _activeRecords.TryGetValue(key, out var v))
            {
                layout = v;
                return true;
            }
            layout = default;
            return false;
        }

        private static bool IsRecord(string fqType) =>
            TryLookupRecord(fqType, out _);

        // Canon-ABI primitive size + alignment.
        private static int PrimitiveSize(string fqType)
        {
            switch (fqType)
            {
                case "byte": case "sbyte": case "bool":
                case "System.Byte": case "System.SByte":
                case "System.Boolean":
                    return 1;
                case "short": case "ushort":
                case "System.Int16": case "System.UInt16":
                    return 2;
                case "int": case "uint": case "float":
                case "System.Int32": case "System.UInt32":
                case "System.Single":
                    return 4;
                case "long": case "ulong": case "double":
                case "System.Int64": case "System.UInt64":
                case "System.Double":
                    return 8;
                case ValueTupleType: return 0;
                default: return 4;
            }
        }

        private static int PrimitiveAlign(string fqType)
        {
            switch (fqType)
            {
                case "byte": case "sbyte": case "bool":
                case "System.Byte": case "System.SByte":
                case "System.Boolean":
                    return 1;
                case "short": case "ushort":
                case "System.Int16": case "System.UInt16":
                    return 2;
                case "int": case "uint": case "float":
                case "System.Int32": case "System.UInt32":
                case "System.Single":
                    return 4;
                case "long": case "ulong": case "double":
                case "System.Int64": case "System.UInt64":
                case "System.Double":
                    return 8;
                case ValueTupleType: return 1;
                default: return 4;
            }
        }

        // Canon-ABI field size/align including aggregate
        // shapes — strings and byte[] (list<u8>) lay out as
        // a (ptr:i32, len:i32) pair: 8 bytes, align 4.
        // option<T>: align_to(1, alignof(T)) + sizeof(T),
        // align = max(1, alignof(T)).
        // result<TOk, TErr>: align_to(1, maxAlign) + maxSize.
        private static int FieldSize(string fqType)
        {
            if (IsPtrLenAggregate(fqType)
                || IsListType(fqType)) return 8;
            if (IsOption(fqType))
            {
                string innerT = InnerOfNullable(fqType);
                return NullablePayloadOffset(innerT)
                    + FieldSize(innerT);
            }
            if (IsWitResult(fqType))
            {
                var (ok, err) = WitResultArgs(fqType);
                var joined = ResultJoinedPayload(ok, err);
                if (joined == null) return 1;
                return ResultPayloadOffset(ok, err)
                    + FieldSize(joined);
            }
            if (TryLookupRecord(fqType, out var rec))
                return RecordSize(rec);
            return PrimitiveSize(fqType);
        }

        private static int FieldAlign(string fqType)
        {
            if (IsPtrLenAggregate(fqType)
                || IsListType(fqType)) return 4;
            if (IsOption(fqType))
            {
                string innerT = InnerOfNullable(fqType);
                return System.Math.Max(1,
                    FieldAlign(innerT));
            }
            if (IsWitResult(fqType))
            {
                var (ok, err) = WitResultArgs(fqType);
                int armAlign = System.Math.Max(
                    FieldAlign(ok), FieldAlign(err));
                return System.Math.Max(1, armAlign);
            }
            if (TryLookupRecord(fqType, out var rec))
                return RecordAlign(rec);
            return PrimitiveAlign(fqType);
        }

        // Flat-slot count for a field type. Primitives are 1
        // slot; string / byte[] are 2 (ptr + len); option<T>
        // is 2 (disc + payload); result<TOk, TErr> is 1 if
        // both arms unit, else 2 (disc + joined payload);
        // nested record is the sum of its fields' slots.
        private static int FieldSlotCount(string fqType)
        {
            if (IsPtrLenAggregate(fqType)
                || IsListType(fqType)) return 2;
            if (IsOption(fqType))
            {
                // option<T> flat: disc + inner-flat-slots.
                // Primitive inner = 1 slot; ptr/len inner = 2.
                string innerT = InnerOfNullable(fqType);
                return 1 + FieldSlotCount(innerT);
            }
            if (IsWitResult(fqType))
            {
                var (ok, err) = WitResultArgs(fqType);
                var joined = ResultJoinedPayload(ok, err);
                if (joined == null) return 1;
                // disc + payload slots. Ptr/len payload
                // contributes 2 slots (ptr, len); primitive
                // payload contributes 1.
                return 1 + (IsPtrLenAggregate(joined) ? 2 : 1);
            }
            if (TryLookupRecord(fqType, out var rec))
                return TotalFlatSlotsForRecord(rec);
            return 1;
        }

        // Canon-ABI record memory size: last field's end,
        // rounded up to the record's alignment. Empty records
        // wouldn't pass BuildRecordLayout so we don't handle
        // that here.
        private static int RecordSize(RecordLayout rec)
        {
            var last = rec.Fields[rec.Fields.Length - 1];
            int end = last.Offset + FieldSize(last.Type);
            return AlignTo(end, RecordAlign(rec));
        }

        private static int RecordAlign(RecordLayout rec)
        {
            int a = 1;
            foreach (var f in rec.Fields)
            {
                int fa = FieldAlign(f.Type);
                if (fa > a) a = fa;
            }
            return a;
        }

        private static int AlignTo(int offset, int alignment)
        {
            int rem = offset % alignment;
            return rem == 0 ? offset : offset + (alignment - rem);
        }

        /// <summary>
        /// Compute the C# type of the canon-ABI joined payload
        /// slot for a <c>result&lt;TOk, TErr&gt;</c>. Returns:
        /// <list type="bullet">
        ///   <item><c>null</c> — both arms are unit; flat
        ///     lowering has no payload slot (just the disc).</item>
        ///   <item>the primitive's type — one arm is unit, the
        ///     other is the named primitive.</item>
        ///   <item>the primitive's type — both arms are the
        ///     same primitive type.</item>
        ///   <item><c>"MIXED"</c> — different-kind / different-
        ///     width primitives need the canon-ABI join rule
        ///     which we don't emit for the MVP.</item>
        /// </list>
        /// </summary>
        private static string? ResultJoinedPayload(
            string ok, string err)
        {
            bool okUnit = ok == ValueTupleType;
            bool errUnit = err == ValueTupleType;
            if (okUnit && errUnit) return null;
            if (okUnit) return err;
            if (errUnit) return ok;
            if (ok == err) return ok;
            // Different ptr/len arms (e.g., string + byte[])
            // share the SAME (ptr, len) memory slots — the
            // per-arm lift/lower dispatches on the arm's C#
            // type rather than the joined value, so we can
            // pick either arm here as the "ptr/len signal".
            if (IsPtrLenAggregate(ok)
                && IsPtrLenAggregate(err))
                return ok;
            // Mixed primitive ↔ ptr/len: take the ptr/len
            // type as the joined-payload signal. Both arms
            // then share the (ptr, len) slot pair; the
            // primitive arm uses slot 1 as the primitive
            // value and leaves slot 2 at zero. Restricted to
            // i32-or-smaller primitive arms — wider
            // primitives need joined-slot widening that we
            // don't emit yet.
            if (IsPtrLenAggregate(ok)
                && IsPrimitive(err)
                && PrimitiveSize(err) <= 4)
                return ok;
            if (IsPtrLenAggregate(err)
                && IsPrimitive(ok)
                && PrimitiveSize(ok) <= 4)
                return err;
            return "MIXED";
        }

        private static int ResultPayloadOffset(
            string ok, string err)
        {
            int armAlign = System.Math.Max(
                FieldAlign(ok), FieldAlign(err));
            return AlignTo(1, armAlign);
        }

        // True iff the result type has at least one ptr/len
        // arm (string / byte[]). Used to drive
        // cabi_post emission + memory/realloc requirements.
        private static bool ResultHasPtrLenArm(string fqType)
        {
            if (!IsWitResult(fqType)) return false;
            var (ok, err) = WitResultArgs(fqType);
            return IsPtrLenAggregate(ok)
                || IsPtrLenAggregate(err);
        }

        // True iff any param is a result<...> with a ptr/len
        // arm — drives EmitSyncEnsureMemoryAndRealloc since
        // the active arm lowers via realloc.
        private static bool AnyResultParamWithPtrLenArm(
            ExportMethod ex)
        {
            foreach (var p in ex.Parameters)
                if (IsWitResult(p.Type)
                    && ResultHasPtrLenArm(p.Type))
                    return true;
            return false;
        }

        // True iff `fqType` is option<T> where T is ptr/len
        // (string / byte[]).
        private static bool IsOptionOfPtrLen(string fqType)
            => IsOption(fqType)
                && IsPtrLenAggregate(
                    InnerOfNullable(fqType));

        // True iff any param is option<string> / option<byte[]>
        // — drives memory + realloc allocation just like a
        // top-level ptr/len param.
        private static bool AnyOptionParamWithPtrLenInner(
            ExportMethod ex)
        {
            foreach (var p in ex.Parameters)
                if (IsOptionOfPtrLen(p.Type)) return true;
            return false;
        }

        // Strip the trailing '?' from `T?` to get the inner T
        // for codegen of the payload slot type.
        private static string InnerOfNullable(string fqType) =>
            fqType.EndsWith("?", StringComparison.Ordinal)
                ? fqType.Substring(0, fqType.Length - 1)
                : fqType;

        private static bool IsSupportedParam(string fqType) =>
            IsPrimitive(fqType)
            || IsString(fqType)
            || IsPrimitiveArray(fqType)
            || IsListType(fqType)
            || IsOption(fqType)
            || IsSupportedResult(fqType)
            || IsSupportedTuple(fqType)
            || IsRecord(fqType);

        private static bool IsSupportedReturn(string? fqType) =>
            fqType == null
            || IsPrimitive(fqType)
            || IsString(fqType)
            || IsPrimitiveArray(fqType)
            || IsListType(fqType)
            || IsOption(fqType)
            || IsSupportedResult(fqType)
            || IsSupportedTuple(fqType)
            || IsRecord(fqType);

        private static ScanResult CollectAndDiagnose(
            Compilation compilation)
        {
            var harnessAttr = compilation.GetTypeByMetadataName(
                HarnessAttributeFqn);
            var exportAttr = compilation.GetTypeByMetadataName(
                ExportAttributeFqn);
            var syncExportAttr =
                compilation.GetTypeByMetadataName(
                    SyncExportAttributeFqn);
            var witRecordAttr =
                compilation.GetTypeByMetadataName(
                    WitRecordAttributeFqn);
            if (harnessAttr == null || exportAttr == null)
                return new ScanResult(
                    ImmutableArray<HarnessClass>.Empty,
                    ImmutableArray<Diagnostic>.Empty,
                    System.Collections.Immutable.ImmutableDictionary<
                        string, RecordLayout>.Empty);

            // Discover [WitRecord]-marked types and compute
            // their canon-ABI layouts. Nested records (a record
            // field whose type is itself a [WitRecord]) are
            // resolved via on-demand recursion: gather the
            // candidate set first so IsSupportedFieldType /
            // IsRecord can accept any [WitRecord], then build
            // layouts in dependency order using a mutable
            // dictionary that _activeRecords reads through.
            var candidates = new System.Collections.Generic
                .Dictionary<string, INamedTypeSymbol>();
            if (witRecordAttr != null)
            {
                foreach (var type in EnumerateAllTypes(
                    compilation.Assembly.GlobalNamespace))
                {
                    if (!HasAttribute(type, witRecordAttr))
                        continue;
                    string fqn = StripGlobal(type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat));
                    candidates[fqn] = type;
                }
            }
            var built = new System.Collections.Generic
                .Dictionary<string, RecordLayout>();
            // Make in-progress builds visible to FieldAlign /
            // FieldSize / IsSupportedFieldType.
            _activeRecords = built;
            var inProgress = new System.Collections.Generic
                .HashSet<string>();
            foreach (var kv in candidates)
            {
                BuildRecordLayoutRecursive(kv.Value,
                    candidates, built, inProgress);
            }
            var recordMap = System.Collections.Immutable
                .ImmutableDictionary
                .CreateRange(built);
            // Make records visible to IsSupportedParam /
            // IsSupportedReturn / EmitExportMethod during
            // the harness-class scan below.
            _activeRecords = recordMap;

            var entries = ImmutableArray.CreateBuilder<HarnessClass>();
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            foreach (var type in EnumerateAllTypes(
                compilation.Assembly.GlobalNamespace))
            {
                if (!HasAttribute(type, harnessAttr)) continue;

                // Diagnostic: class must be declared `partial`
                // so the emitted body can compile alongside the
                // user's declaration. Roslyn surfaces this via
                // each declaration's syntax — if any declaration
                // is missing `partial` we error.
                bool anyNonPartial = false;
                foreach (var declRef in type.DeclaringSyntaxReferences)
                {
                    var node = declRef.GetSyntax();
                    if (node is Microsoft.CodeAnalysis.CSharp.Syntax
                        .ClassDeclarationSyntax cls)
                    {
                        if (!cls.Modifiers.Any(m =>
                            m.IsKind(Microsoft.CodeAnalysis.CSharp
                                .SyntaxKind.PartialKeyword)))
                        {
                            anyNonPartial = true;
                            diagnostics.Add(Diagnostic.Create(
                                HarnessClassMustBePartial,
                                cls.Identifier.GetLocation(),
                                type.ToDisplayString()));
                        }
                    }
                }
                if (anyNonPartial) continue;

                var exports = ImmutableArray.CreateBuilder<ExportMethod>();
                foreach (var member in type.GetMembers())
                {
                    if (member is not IMethodSymbol method) continue;
                    ExportKind kind = ExportKind.Async;
                    string? exportName = null;
                    bool found = false;
                    foreach (var attr in method.GetAttributes())
                    {
                        bool isAsync = SymbolEqualityComparer.Default
                            .Equals(attr.AttributeClass, exportAttr);
                        bool isSync = syncExportAttr != null
                            && SymbolEqualityComparer.Default.Equals(
                                attr.AttributeClass, syncExportAttr);
                        if (!isAsync && !isSync) continue;
                        found = true;
                        kind = isSync
                            ? ExportKind.Sync : ExportKind.Async;
                        if (attr.ConstructorArguments.Length > 0
                            && attr.ConstructorArguments[0].Value
                                is string en
                            && !string.IsNullOrEmpty(en))
                        {
                            exportName = en;
                        }
                        break;
                    }
                    if (!found) continue;
                    if (exportName == null) continue;

                    // Diagnostic: method must be a partial
                    // definition so the generator can emit the
                    // body. Non-partial methods already have a
                    // user-provided body that the generator
                    // would conflict with.
                    if (!method.IsPartialDefinition)
                    {
                        var loc = method.Locations.Length > 0
                            ? method.Locations[0] : Location.None;
                        diagnostics.Add(Diagnostic.Create(
                            ExportMethodMustBePartial, loc,
                            method.Name,
                            type.ToDisplayString()));
                        continue;
                    }

                    var parameters = method.Parameters
                        .Select(p => new ExportParam(
                            p.Name,
                            TypeDisplayWithNullability(p.Type)))
                        .ToImmutableArray();
                    string? returnType = method.ReturnsVoid
                        ? null
                        : TypeDisplayWithNullability(
                            method.ReturnType);

                    exports.Add(new ExportMethod(
                        method.Name, exportName,
                        AccessibilityKeyword(method.DeclaredAccessibility),
                        parameters, returnType, kind));
                }

                string ns = type.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : type.ContainingNamespace.ToDisplayString();
                entries.Add(new HarnessClass(
                    ns, type.Name,
                    AccessibilityKeyword(type.DeclaredAccessibility),
                    exports.ToImmutable(),
                    recordMap));
            }

            return new ScanResult(
                entries
                    .OrderBy(e => e.GeneratedFileName,
                        StringComparer.Ordinal)
                    .ToImmutableArray(),
                diagnostics.ToImmutable(),
                recordMap);
        }

        // Compute the canon-ABI record layout for a
        // [WitRecord]-marked type. Walks the type's instance
        // fields in declaration order, computes offsets via
        // align_to. Returns null when any field is not a
        // primitive (the MVP doesn't yet handle aggregate
        // fields in records).
        private static RecordLayout? BuildRecordLayoutRecursive(
            INamedTypeSymbol type,
            System.Collections.Generic
                .IDictionary<string, INamedTypeSymbol> candidates,
            System.Collections.Generic
                .Dictionary<string, RecordLayout> built,
            System.Collections.Generic.HashSet<string> inProgress)
        {
            string fqn = StripGlobal(type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));
            if (built.TryGetValue(fqn, out var existing))
                return existing;
            if (!inProgress.Add(fqn))
                return null; // direct/indirect cycle — punt
            try
            {
                var fields = System.Collections.Immutable
                    .ImmutableArray.CreateBuilder<RecordField>();
                int offset = 0;
                foreach (var member in type.GetMembers())
                {
                    if (member is not IFieldSymbol field)
                        continue;
                    if (field.IsStatic) continue;
                    if (field.IsImplicitlyDeclared) continue;
                    string ft = StripGlobal(
                        TypeDisplayWithNullability(
                            field.Type));
                    // Recursively build the sub-record's layout
                    // first so FieldAlign / FieldSize can read
                    // it when laying this field out.
                    if (candidates.TryGetValue(ft,
                            out var subType)
                        && !built.ContainsKey(ft))
                    {
                        var sub = BuildRecordLayoutRecursive(
                            subType, candidates, built,
                            inProgress);
                        if (sub == null) return null;
                        built[ft] = sub.Value;
                    }
                    if (!IsSupportedFieldType(ft))
                        return null;
                    offset = AlignTo(offset, FieldAlign(ft));
                    fields.Add(new RecordField(
                        field.Name, ft, offset));
                    offset += FieldSize(ft);
                }
                if (fields.Count == 0) return null;
                var layout = new RecordLayout(fqn,
                    fields.ToImmutable());
                built[fqn] = layout;
                return layout;
            }
            finally
            {
                inProgress.Remove(fqn);
            }
        }

        private static bool HasAttribute(
            INamedTypeSymbol type, INamedTypeSymbol attrSymbol)
        {
            foreach (var attr in type.GetAttributes())
                if (SymbolEqualityComparer.Default.Equals(
                        attr.AttributeClass, attrSymbol))
                    return true;
            return false;
        }

        private static string AccessibilityKeyword(
            Accessibility access) => access switch
            {
                Accessibility.Public => "public",
                Accessibility.Internal => "internal",
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedAndInternal =>
                    "private protected",
                Accessibility.ProtectedOrInternal =>
                    "protected internal",
                _ => "internal",
            };

        private static IEnumerable<INamedTypeSymbol> EnumerateAllTypes(
            INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
            {
                yield return t;
                foreach (var nested in EnumerateNested(t))
                    yield return nested;
            }
            foreach (var sub in ns.GetNamespaceMembers())
                foreach (var t in EnumerateAllTypes(sub))
                    yield return t;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNested(
            INamedTypeSymbol type)
        {
            foreach (var nested in type.GetTypeMembers())
            {
                yield return nested;
                foreach (var grandchild in EnumerateNested(nested))
                    yield return grandchild;
            }
        }

        private static string EmitHarness(HarnessClass cls)
        {
            _activeRecords = cls.Records;
            _listInnerCounter = 0;
            try
            {
                return EmitHarnessInner(cls);
            }
            finally
            {
                _activeRecords = null;
            }
        }

        private static string EmitHarnessInner(HarnessClass cls)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Source generator: " +
                "Wacs.ComponentModel.Async.SourceGen" +
                ".AsyncComponentHarnessGenerator");
            sb.AppendLine("// Emits AOT-safe constructor + " +
                "InvokeCoreAsyncLift wiring for each [AsyncExport].");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Wacs.Core.Runtime;");
            sb.AppendLine();

            bool hasNs = !string.IsNullOrEmpty(cls.Namespace);
            if (hasNs)
            {
                sb.Append("namespace ");
                sb.AppendLine(cls.Namespace);
                sb.AppendLine("{");
            }

            sb.Append("    ");
            sb.Append(cls.Accessibility);
            sb.Append(" partial class ");
            sb.AppendLine(cls.ClassName);
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly global::"
                + ComponentInstanceFqn + " _instance;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>The underlying " +
                "<see cref=\"global::"
                + ComponentInstanceFqn + "\"/> — surfaced so " +
                "consumers can reach the dispatcher / host " +
                "directly when needed.</summary>");
            sb.AppendLine("        public global::"
                + ComponentInstanceFqn + " Instance => _instance;");
            sb.AppendLine();
            sb.Append("        ");
            sb.Append(cls.Accessibility);
            sb.Append(' ');
            sb.Append(cls.ClassName);
            sb.AppendLine("(byte[] componentBytes,");
            sb.AppendLine("            System.Action<global::Wacs" +
                ".Core.Runtime.WasmRuntime>? configureImports = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            _instance = global::"
                + ComponentInstanceFqn
                + ".InstantiateAot(componentBytes, configureImports);");
            sb.AppendLine("        }");

            // Memoized invoker fields for sync exports — one
            // per method, lazily resolved on first invocation.
            // Class-scope so they live as long as the harness
            // instance.
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind != ExportKind.Sync) continue;
                sb.Append("        private ");
                sb.Append(BuildInvokerDelegateType(ex));
                sb.Append(" _invoker_");
                sb.Append(ex.MethodName);
                sb.AppendLine(";");
            }

            // Class-scope marshaling state for aggregate
            // (string / byte[] / option<T>) — memory +
            // optionally cabi_realloc invoker + per-method
            // cabi_post invokers.
            //
            // Splitting needs:
            //   * _memory — any aggregate (ptr/len OR
            //     option<T> with retArea-style return).
            //   * _reallocInvoke — only ptr/len aggregates
            //     (string / byte[]) need wasm-side allocation.
            //   * _post_<MethodName> — only string / byte[]
            //     returns need the post-return free hook.
            bool needsMemory = false;
            bool needsRealloc = false;
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind != ExportKind.Sync) continue;
                if (AnyPtrLenAggregate(ex))
                {
                    needsMemory = true;
                    needsRealloc = true;
                }
                // Any result<...> param with a ptr/len arm or
                // option<string|byte[]> param needs realloc
                // to lower the active value into wasm memory;
                // memory follows automatically.
                foreach (var p in ex.Parameters)
                {
                    if ((IsWitResult(p.Type)
                            && ResultHasPtrLenArm(p.Type))
                        || IsOptionOfPtrLen(p.Type))
                    {
                        needsMemory = true;
                        needsRealloc = true;
                    }
                }
                // Aggregate fields nested inside tuple/record
                // params require both memory and cabi_realloc
                // (each field lowers like a top-level string /
                // byte[] param).
                if (AnyAggregateFieldInTupleOrRecord(ex))
                {
                    needsMemory = true;
                    foreach (var p in ex.Parameters)
                        if (AggregateContainsPtrLen(p.Type))
                            needsRealloc = true;
                    // Returns that contain aggregates don't
                    // need cabi_realloc (callee owns the
                    // retArea) but do need memory.
                }
                if (ex.ReturnType != null
                    && IsOption(ex.ReturnType))
                {
                    needsMemory = true;
                }
                if (ex.ReturnType != null
                    && IsWitResult(ex.ReturnType))
                {
                    var (ok, err) = WitResultArgs(ex.ReturnType);
                    if (ResultJoinedPayload(ok, err) != null)
                        needsMemory = true;
                }
                if (ex.ReturnType != null
                    && IsTuple(ex.ReturnType)
                    && TotalFlatSlotsForTuple(ex.ReturnType) > 1)
                {
                    needsMemory = true;
                }
                if (ex.ReturnType != null
                    && TryLookupRecord(ex.ReturnType,
                        out var recM)
                    && TotalFlatSlotsForRecord(recM) > 1)
                {
                    needsMemory = true;
                }
            }
            if (needsMemory)
            {
                sb.AppendLine(
                    "        private global::Wacs.Core.Runtime.Types" +
                    ".MemoryInstance? _memory;");
            }
            if (needsRealloc)
            {
                sb.AppendLine(
                    "        private System.Func<int, int, int, int, int>? " +
                    "_reallocInvoke;");
            }
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind != ExportKind.Sync) continue;
                if (ex.ReturnType == null) continue;
                bool needsPost =
                    IsPtrLenAggregate(ex.ReturnType)
                    || IsListType(ex.ReturnType)
                    || ((IsTuple(ex.ReturnType)
                            || TryLookupRecord(ex.ReturnType,
                                out _))
                        && AggregateContainsPtrLen(ex.ReturnType))
                    || (IsWitResult(ex.ReturnType)
                        && ResultHasPtrLenArm(ex.ReturnType))
                    || IsOptionOfPtrLen(ex.ReturnType);
                if (!needsPost) continue;
                sb.Append(
                    "        private System.Action<int>? _post_");
                sb.Append(ex.MethodName);
                sb.AppendLine(";");
            }

            foreach (var ex in cls.Exports)
            {
                sb.AppendLine();
                EmitExportMethod(sb, ex);
            }

            sb.AppendLine("    }");
            if (hasNs) sb.AppendLine("}");
            return sb.ToString();
        }

        // Emit one partial method body. Supports void return
        // and primitive (canon-ABI flat) return types; primitive
        // (canon-ABI flat) parameter types are boxed and passed
        // verbatim to InvokeCoreAsyncLift. Non-primitive types
        // emit a `#error` directive that fires at consumer
        // compile time — points at the partial method that
        // needs hand-marshaling until the typed lift/lower
        // codegen extension lands.
        private static void EmitExportMethod(
            StringBuilder sb, ExportMethod ex)
        {
            // Validate types up front so the error message
            // identifies the offending parameter / return type.
            // Async exports currently only handle primitives —
            // string lower-into-args + lift-from-task-return
            // requires task.return wiring not yet emitted by
            // the generator. Sync exports get the full
            // primitive + string surface via
            // StringCoding.LowerUtf8 / LiftUtf8 + cabi_realloc.
            bool isSync = ex.Kind == ExportKind.Sync;
            foreach (var p in ex.Parameters)
            {
                bool ok = isSync
                    ? IsSupportedParam(p.Type)
                    : IsPrimitive(p.Type);
                if (!ok)
                {
                    sb.Append("        #error ");
                    sb.Append(
                        $"[{(isSync ? "SyncExport" : "AsyncExport")}] " +
                        $"parameter '{p.Name}' on {ex.MethodName} " +
                        $"has unsupported type '{p.Type}'. " +
                        (isSync
                            ? "Sync exports support primitives + " +
                              "string today; list/aggregate lift-" +
                              "lower codegen lands next."
                            : "Async exports only support canon-" +
                              "ABI primitives today (task.return " +
                              "string codegen pending)."));
                    sb.AppendLine();
                    return;
                }
            }
            if (ex.ReturnType != null)
            {
                bool ok = isSync
                    ? IsSupportedReturn(ex.ReturnType)
                    : IsPrimitive(ex.ReturnType);
                if (!ok)
                {
                    sb.Append("        #error ");
                    sb.Append(
                        $"[{(isSync ? "SyncExport" : "AsyncExport")}] " +
                        $"return type '{ex.ReturnType}' on " +
                        $"{ex.MethodName} is unsupported. " +
                        (isSync
                            ? "Sync exports support primitives + " +
                              "string today."
                            : "Async exports only support canon-" +
                              "ABI primitives today."));
                    sb.AppendLine();
                    return;
                }
            }

            sb.Append("        ");
            sb.Append(ex.Accessibility);
            sb.Append(" partial ");
            sb.Append(ex.ReturnType ?? "void");
            sb.Append(' ');
            sb.Append(ex.MethodName);
            sb.Append('(');
            for (int i = 0; i < ex.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ex.Parameters[i].Type);
                sb.Append(' ');
                sb.Append(ex.Parameters[i].Name);
            }
            sb.AppendLine(")");
            sb.AppendLine("        {");

            // The args array dance: InvokeCoreAsyncLift takes
            // `params object?[]`. Primitive args box. For zero
            // args we pass Array.Empty<object?>() to avoid an
            // allocation on every call.
            string argsExpr;
            if (ex.Parameters.Length == 0)
            {
                argsExpr = "System.Array.Empty<object?>()";
            }
            else
            {
                var argsBuilder = new StringBuilder();
                argsBuilder.Append("new object?[] { ");
                for (int i = 0; i < ex.Parameters.Length; i++)
                {
                    if (i > 0) argsBuilder.Append(", ");
                    argsBuilder.Append(ex.Parameters[i].Name);
                }
                argsBuilder.Append(" }");
                argsExpr = argsBuilder.ToString();
            }

            if (ex.Kind == ExportKind.Async)
            {
                EmitAsyncExportBody(sb, ex, argsExpr);
            }
            else
            {
                EmitSyncExportBody(sb, ex);
            }
            sb.AppendLine("        }");
        }

        private static void EmitAsyncExportBody(
            StringBuilder sb, ExportMethod ex, string argsExpr)
        {
            if (ex.ReturnType == null)
            {
                sb.Append("            _instance.InvokeCoreAsyncLift(\"");
                sb.Append(EscapeStringLiteral(ex.ExportName));
                sb.Append("\", ");
                sb.Append(argsExpr);
                sb.AppendLine(");");
            }
            else
            {
                sb.Append("            var __result = ");
                sb.Append("_instance.InvokeCoreAsyncLift(\"");
                sb.Append(EscapeStringLiteral(ex.ExportName));
                sb.Append("\", ");
                sb.Append(argsExpr);
                sb.AppendLine(");");
                sb.Append("            return (");
                sb.Append(ex.ReturnType);
                sb.AppendLine(")__result!;");
            }
        }

        // Sync exports route through
        // <c>WasmRuntime.CreateInvokerFunc&lt;...&gt;</c> /
        // <c>CreateInvokerAction&lt;...&gt;</c> with statically
        // generic type args derived from the partial method's
        // canon-ABI flat signature (per-parameter ints for
        // strings; statically-known primitive types otherwise).
        // String params lower via cabi_realloc + UTF-8 encode;
        // string returns lift via memory-read + UTF-8 decode +
        // cabi_post_X cleanup.
        private static void EmitSyncExportBody(
            StringBuilder sb, ExportMethod ex)
        {
            // Memory + cabi_realloc state are needed for any
            // aggregate that lowers via memory (string,
            // byte[]) — at the top level OR nested inside a
            // tuple/record param. Also: a result param with
            // a ptr/len arm lowers via realloc inside one of
            // its branches.
            bool needsMemory = AnyPtrLenAggregate(ex)
                || AnyAggregateFieldInTupleOrRecord(ex)
                || AnyResultParamWithPtrLenArm(ex)
                || AnyOptionParamWithPtrLenInner(ex);
            if (needsMemory)
            {
                EmitSyncEnsureMemoryAndRealloc(sb);
            }
            // option<T> + result<T,E> returns go through a
            // retArea — need memory but no realloc (callee
            // owns the retArea; no caller-side free).
            bool needsMemoryForRetArea =
                ex.ReturnType != null
                && (IsOption(ex.ReturnType)
                    || (IsWitResult(ex.ReturnType)
                        && ResultJoinedPayload(
                            WitResultArgs(ex.ReturnType).Ok,
                            WitResultArgs(ex.ReturnType).Err)
                        != null)
                    || (IsTuple(ex.ReturnType)
                        && TotalFlatSlotsForTuple(
                            ex.ReturnType) > 1)
                    || (TryLookupRecord(ex.ReturnType,
                            out var recExN)
                        && TotalFlatSlotsForRecord(recExN) > 1));
            if (needsMemoryForRetArea && !needsMemory)
            {
                EmitSyncEnsureMemoryOnly(sb);
            }

            string field = "_invoker_" + ex.MethodName;
            string createMethod = ex.ReturnType == null
                ? "CreateInvokerAction"
                : "CreateInvokerFunc";

            // Lazy resolve the core invoker. For string-bearing
            // methods the flat signature differs from the
            // declared C# signature — strings flatten to (ptr,
            // len) pairs and a string return flattens to (ptr,
            // len) emitted into the retArea (returning a single
            // i32 retArea pointer).
            sb.Append("            if (");
            sb.Append(field);
            sb.AppendLine(" == null)");
            sb.AppendLine("            {");
            sb.Append("                if (!_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"");
            sb.Append(EscapeStringLiteral(ex.ExportName));
            sb.AppendLine("\", out var __addr))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.Append("                        \"Missing export '");
            sb.Append(EscapeStringLiteral(ex.ExportName));
            sb.AppendLine("'.\");");
            sb.Append("                ");
            sb.Append(field);
            sb.Append(" = _instance.CoreRuntime.");
            sb.Append(createMethod);
            sb.Append(BuildFlatInvokerTypeArgs(ex));
            sb.AppendLine("(__addr);");
            sb.AppendLine("            }");

            // Lower aggregate (string / byte[]) params into
            // wasm memory before the call. Each
            // `(string foo)` / `(byte[] foo)` becomes
            // `(int __foo_ptr, int __foo_len)`.
            foreach (var p in ex.Parameters)
            {
                if (IsString(p.Type))
                {
                    sb.Append(
                        "            global::Wacs.ComponentModel" +
                        ".Harness.StringCoding.LowerUtf8(_memory!, ");
                    sb.Append(p.Name);
                    sb.Append(
                        ", _reallocInvoke!, out int __");
                    sb.Append(p.Name);
                    sb.Append("_ptr, out int __");
                    sb.Append(p.Name);
                    sb.AppendLine("_len);");
                }
                else if (IsPrimitiveArray(p.Type))
                {
                    // canon-ABI list<T>: align = alignof(T),
                    // element size = sizeof(T). realloc size
                    // = len * sizeof(T); BlockCopy bytes too.
                    EmitPrimitiveArrayLower(sb, p.Type,
                        "__" + p.Name, p.Name);
                }
                else if (IsListType(p.Type))
                {
                    // list<string> / list<record>: outer
                    // alloc then per-element write loop.
                    EmitListLower(sb, p.Type,
                        "__" + p.Name, p.Name);
                }
                else if (IsOption(p.Type))
                {
                    // option<T> param lowering: shared helper
                    // emits (disc, payload) for primitive T or
                    // (disc, ptr, len) for ptr/len T.
                    EmitOptionArgLower(sb, p.Type,
                        "__" + p.Name, p.Name);
                }
                else if (IsWitResult(p.Type))
                {
                    // result<TOk, TErr> param lowering routes
                    // through the shared helper so primitive
                    // + ptr/len + unit arm combinations all
                    // produce the right (disc, payload) /
                    // (disc, ptr, len) local triple.
                    EmitResultArgLower(sb, p.Type,
                        "__" + p.Name, p.Name);
                }
                else if (IsTuple(p.Type))
                {
                    // tuple<T1, T2, ...> param lowering:
                    // extract each Item via the ValueTuple's
                    // 1-indexed accessors. Primitive elements
                    // become a typed local; ptr/len aggregate
                    // elements (string, byte[]) lower via
                    // realloc + StringCoding/byte-copy and
                    // bind (ptr, len) locals.
                    var elems = TupleElementTypes(p.Type);
                    for (int i = 0; i < elems.Count; i++)
                    {
                        EmitAggregateFieldLower(sb,
                            elems[i],
                            "__" + p.Name + "_item" + (i + 1),
                            p.Name + ".Item" + (i + 1));
                    }
                }
                else if (TryLookupRecord(p.Type, out var rec))
                {
                    // record param lowering: access each
                    // field by its declared name.
                    foreach (var f in rec.Fields)
                    {
                        EmitAggregateFieldLower(sb,
                            f.Type,
                            "__" + p.Name + "_" + f.Name,
                            p.Name + "." + f.Name);
                    }
                }
            }

            // Call the underlying invoker with the flattened
            // args. ptr/len aggregates + option<T> expand
            // multi-slot; primitives pass straight through.
            sb.Append("            ");
            if (ex.ReturnType != null)
                sb.Append("var __raw = ");
            sb.Append(field);
            sb.Append('(');
            EmitFlatArgsList(sb, ex);
            sb.AppendLine(");");

            // If the export returned a primitive, just cast +
            // return. If it returned a multi-slot aggregate
            // (string / byte[] / option), read the tuple out
            // of the retArea, lift, and call cabi_post_<exp>
            // to release the retArea (only for ptr/len ags;
            // option<T> for primitive T uses inline retArea
            // — no allocation, no post-return needed).
            if (ex.ReturnType != null)
            {
                if (IsString(ex.ReturnType))
                    EmitSyncStringReturnLift(sb, ex);
                else if (IsPrimitiveArray(ex.ReturnType))
                    EmitSyncPrimitiveArrayReturnLift(sb, ex);
                else if (IsListType(ex.ReturnType))
                    EmitSyncListReturnLift(sb, ex);
                else if (IsOption(ex.ReturnType))
                    EmitSyncOptionReturnLift(sb, ex);
                else if (IsWitResult(ex.ReturnType))
                    EmitSyncResultReturnLift(sb, ex);
                else if (IsTuple(ex.ReturnType))
                    EmitSyncTupleReturnLift(sb, ex);
                else if (TryLookupRecord(ex.ReturnType,
                    out var recRet))
                    EmitSyncRecordReturnLift(
                        sb, ex, recRet);
                else
                    sb.AppendLine("            return __raw;");
            }
        }

        // record return lift. Multi-field records use the
        // retArea pointer (`__raw`); single-field records
        // return the field's slot value directly via __raw.
        // Build the record via a property-initializer block
        // — keeps the generated source readable.
        private static void EmitSyncRecordReturnLift(
            StringBuilder sb, ExportMethod ex,
            RecordLayout rec)
        {
            bool hasAggregateField = false;
            foreach (var f in rec.Fields)
                if (IsPtrLenAggregate(f.Type))
                    hasAggregateField = true;

            int totalSlots = 0;
            foreach (var f in rec.Fields)
                totalSlots += FieldSlotCount(f.Type);

            if (totalSlots == 1)
            {
                // Single-slot return: __raw carries the field's
                // slot value directly. Either a primitive field
                // OR a single result<(), ()> field (disc-only).
                var only = rec.Fields[0];
                sb.Append("            return new ");
                sb.Append(ex.ReturnType);
                sb.Append(" { ");
                sb.Append(only.Name);
                sb.Append(" = ");
                if (IsWitResult(only.Type))
                {
                    sb.Append("__raw == 0 ? ");
                    sb.Append(only.Type);
                    sb.Append(".Ok(default) : ");
                    sb.Append(only.Type);
                    sb.Append(".Err(default)");
                }
                else
                {
                    sb.Append("__raw");
                }
                sb.AppendLine(" };");
                return;
            }

            // Multi-slot return — retArea pointed at by __raw.
            // Bind per-field locals for non-primitive fields
            // (aggregate / option / result), then build the
            // record from those locals + inline primitive reads.
            foreach (var f in rec.Fields)
                EmitFieldLiftLocalsIfNeeded(sb, f.Type,
                    "__rec_" + f.Name, f.Offset);

            sb.Append("            var __recResult = new ");
            sb.Append(ex.ReturnType);
            sb.Append(" { ");
            bool first = true;
            foreach (var f in rec.Fields)
            {
                if (!first) sb.Append(", ");
                sb.Append(f.Name);
                sb.Append(" = ");
                sb.Append(FieldLiftExpression(f.Type,
                    "__rec_" + f.Name, f.Offset));
                first = false;
            }
            sb.AppendLine(" };");
            if (hasAggregateField)
                EmitCabiPostInvoke(sb, ex);
            sb.AppendLine("            return __recResult;");
        }

        // Emit any locals required to lift a tuple-element /
        // record-field of `fieldType` at `offset` within the
        // current retArea. Primitives have no separate local —
        // they're read inline by FieldLiftExpression. ptr/len
        // aggregates, option<T>, and result<TOk, TErr> bind a
        // typed local that the result builder references.
        private static void EmitFieldLiftLocalsIfNeeded(
            StringBuilder sb, string fieldType,
            string localName, int offset)
        {
            if (IsPtrLenAggregate(fieldType))
                EmitAggregateFieldLiftLocal(sb, fieldType,
                    localName, offset);
            else if (IsListType(fieldType))
                EmitListFieldLiftLocal(sb, fieldType,
                    localName, offset);
            else if (IsOption(fieldType))
                EmitOptionFieldLiftLocal(sb, fieldType,
                    localName, offset);
            else if (IsWitResult(fieldType))
                EmitResultFieldLiftLocal(sb, fieldType,
                    localName, offset);
            else if (TryLookupRecord(fieldType, out var subRec))
                EmitRecordFieldLiftLocal(sb, fieldType, subRec,
                    localName, offset);
        }

        // Lift a list<string> / list<record> field at the
        // given retArea offset into a `<fieldType> localName`
        // C# local. Reads (ptr, len) from `__raw + offset`,
        // then runs the per-element loop via
        // EmitListLiftStatements.
        private static void EmitListFieldLiftLocal(
            StringBuilder sb, string fieldType,
            string localName, int offset)
        {
            sb.Append("            int ");
            sb.Append(localName);
            sb.Append("_ptr = global::Wacs.ComponentModel" +
                ".Harness.MemoryHelpers.ReadI32LE(_memory!, " +
                "__raw + ");
            sb.Append(offset);
            sb.AppendLine(");");
            sb.Append("            int ");
            sb.Append(localName);
            sb.Append("_len = global::Wacs.ComponentModel" +
                ".Harness.MemoryHelpers.ReadI32LE(_memory!, " +
                "__raw + ");
            sb.Append(offset + 4);
            sb.AppendLine(");");
            EmitListLiftStatements(sb, fieldType, localName,
                localName + "_ptr", localName + "_len");
        }

        // Build the C# expression that lifts a field of
        // `fieldType` at `offset` within the current retArea.
        // Primitives are read inline; other shapes refer to a
        // local bound by EmitFieldLiftLocalsIfNeeded.
        private static string FieldLiftExpression(
            string fieldType, string localName, int offset)
        {
            if (IsPtrLenAggregate(fieldType)
                || IsListType(fieldType)
                || IsOption(fieldType)
                || IsWitResult(fieldType)
                || TryLookupRecord(fieldType, out _))
                return localName;
            return ReadMemoryExprForType(fieldType,
                "_memory!", "__raw + " + offset);
        }

        // Lift a nested record field. Recursively bind sub-
        // field locals at the absolute retArea offset
        // (`offset + sub.Offset`), then construct the inner
        // record from those.
        private static void EmitRecordFieldLiftLocal(
            StringBuilder sb, string fieldType,
            RecordLayout subRec,
            string localName, int offset)
        {
            foreach (var f in subRec.Fields)
                EmitFieldLiftLocalsIfNeeded(sb, f.Type,
                    localName + "_" + f.Name,
                    offset + f.Offset);
            sb.Append("            ");
            sb.Append(fieldType);
            sb.Append(' ');
            sb.Append(localName);
            sb.Append(" = new ");
            sb.Append(fieldType);
            sb.Append(" { ");
            bool first = true;
            foreach (var f in subRec.Fields)
            {
                if (!first) sb.Append(", ");
                sb.Append(f.Name);
                sb.Append(" = ");
                sb.Append(FieldLiftExpression(f.Type,
                    localName + "_" + f.Name,
                    offset + f.Offset));
                first = false;
            }
            sb.AppendLine(" };");
        }

        // Lift an option<T> field at the given retArea offset
        // into a Nullable<T> (T?) or nullable ref (string? /
        // byte[]?) local. Layout: disc:u8 at offset, payload
        // at offset + NullablePayloadOffset(T). For ptr/len
        // inner, payload is (ptr, len) → lift via
        // StringCoding / LiftBytes when disc != 0.
        private static void EmitOptionFieldLiftLocal(
            StringBuilder sb, string fieldType,
            string localName, int offset)
        {
            string innerT = InnerOfNullable(fieldType);
            int payloadOff = NullablePayloadOffset(innerT);
            sb.Append("            byte ");
            sb.Append(localName);
            sb.Append("_disc = global::Wacs.ComponentModel" +
                ".Harness.MemoryHelpers.ReadU8(_memory!, " +
                "__raw + ");
            sb.Append(offset);
            sb.AppendLine(");");

            if (IsPtrLenAggregate(innerT))
            {
                sb.Append("            int ");
                sb.Append(localName);
                sb.Append("_ptr = global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.ReadI32LE(" +
                    "_memory!, __raw + ");
                sb.Append(offset + payloadOff);
                sb.AppendLine(");");
                sb.Append("            int ");
                sb.Append(localName);
                sb.Append("_len = global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.ReadI32LE(" +
                    "_memory!, __raw + ");
                sb.Append(offset + payloadOff + 4);
                sb.AppendLine(");");
                sb.Append("            ");
                sb.Append(fieldType);
                sb.Append(' ');
                sb.Append(localName);
                sb.Append(" = ");
                sb.Append(localName);
                sb.Append("_disc == 0 ? null : ");
                sb.Append(PtrLenLiftExpression(innerT,
                    localName + "_ptr",
                    localName + "_len"));
                sb.AppendLine(";");
                return;
            }

            sb.Append("            ");
            sb.Append(fieldType);
            sb.Append(' ');
            sb.Append(localName);
            sb.Append(" = ");
            sb.Append(localName);
            sb.Append("_disc == 0 ? (");
            sb.Append(fieldType);
            sb.Append(")null : ");
            sb.Append(ReadMemoryExprForType(innerT,
                "_memory!",
                "__raw + " + (offset + payloadOff)));
            sb.AppendLine(";");
        }

        // Lift a result<TOk, TErr> field at the given retArea
        // offset into a WitResult<TOk, TErr> local. Layout:
        // disc:u8 at offset; payload (when joined != null) at
        // offset + ResultPayloadOffset(ok, err).
        private static void EmitResultFieldLiftLocal(
            StringBuilder sb, string fieldType,
            string localName, int offset)
        {
            var (ok, err) = WitResultArgs(fieldType);
            var joined = ResultJoinedPayload(ok, err);
            sb.Append("            byte ");
            sb.Append(localName);
            sb.Append("_disc = global::Wacs.ComponentModel" +
                ".Harness.MemoryHelpers.ReadU8(_memory!, " +
                "__raw + ");
            sb.Append(offset);
            sb.AppendLine(");");

            if (joined != null && IsPtrLenAggregate(joined))
            {
                // ptr/len joined arms: bind (ptr, len) locals
                // once, then branch on disc to build .Ok /
                // .Err with the appropriate lift expression
                // per arm type.
                int payloadOff = ResultPayloadOffset(ok, err);
                sb.Append("            int ");
                sb.Append(localName);
                sb.Append("_ptr = global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.ReadI32LE(" +
                    "_memory!, __raw + ");
                sb.Append(offset + payloadOff);
                sb.AppendLine(");");
                sb.Append("            int ");
                sb.Append(localName);
                sb.Append("_len = global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.ReadI32LE(" +
                    "_memory!, __raw + ");
                sb.Append(offset + payloadOff + 4);
                sb.AppendLine(");");
                sb.Append("            ");
                sb.Append(fieldType);
                sb.Append(' ');
                sb.Append(localName);
                sb.AppendLine(";");
                sb.Append("            if (");
                sb.Append(localName);
                sb.AppendLine("_disc == 0)");
                sb.AppendLine("            {");
                sb.Append("                ");
                sb.Append(localName);
                sb.Append(" = ");
                sb.Append(fieldType);
                sb.Append(".Ok(");
                if (ok == ValueTupleType)
                    sb.Append("default");
                else
                    sb.Append(PtrLenLiftExpression(ok,
                        localName + "_ptr",
                        localName + "_len"));
                sb.AppendLine(");");
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
                sb.Append("                ");
                sb.Append(localName);
                sb.Append(" = ");
                sb.Append(fieldType);
                sb.Append(".Err(");
                if (err == ValueTupleType)
                    sb.Append("default");
                else
                    sb.Append(PtrLenLiftExpression(err,
                        localName + "_ptr",
                        localName + "_len"));
                sb.AppendLine(");");
                sb.AppendLine("            }");
                return;
            }

            sb.Append("            ");
            sb.Append(fieldType);
            sb.Append(' ');
            sb.Append(localName);
            sb.Append(" = ");
            sb.Append(localName);
            sb.Append("_disc == 0 ? ");
            sb.Append(fieldType);
            sb.Append(".Ok(");
            if (joined == null || ok == ValueTupleType)
                sb.Append("default");
            else
                sb.Append(ReadMemoryExprForType(ok,
                    "_memory!", "__raw + "
                        + (offset
                            + ResultPayloadOffset(ok, err))));
            sb.Append(") : ");
            sb.Append(fieldType);
            sb.Append(".Err(");
            if (joined == null || err == ValueTupleType)
                sb.Append("default");
            else
                sb.Append(ReadMemoryExprForType(err,
                    "_memory!", "__raw + "
                        + (offset
                            + ResultPayloadOffset(ok, err))));
            sb.AppendLine(");");
        }

        // tuple<T1, T2, ...> return lift. Two cases:
        //   * Single-element tuple — flat is just the
        //     element's slot, returned directly. Wrap in
        //     ValueTuple<T>.
        //   * Multi-element — __raw is the retArea pointer.
        //     Read each field at its canon-ABI offset and
        //     build the tuple.
        private static void EmitSyncTupleReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            var elems = TupleElementTypes(ex.ReturnType!);
            bool hasAggregate = false;
            foreach (var e in elems)
                if (IsPtrLenAggregate(e)) hasAggregate = true;

            int totalSlots = 0;
            foreach (var e in elems)
                totalSlots += FieldSlotCount(e);

            if (totalSlots == 1)
            {
                // Single-slot return: __raw carries the only
                // element's slot value. Primitive: return it
                // directly. result<(), ()>: build from disc.
                var only = elems[0];
                sb.Append("            return (");
                if (IsWitResult(only))
                {
                    sb.Append("__raw == 0 ? ");
                    sb.Append(only);
                    sb.Append(".Ok(default) : ");
                    sb.Append(only);
                    sb.Append(".Err(default)");
                }
                else
                {
                    sb.Append("__raw");
                }
                sb.AppendLine(");");
                return;
            }

            // Multi-slot return — retArea pointed at by __raw.
            // Compute canon-ABI offsets for each field using
            // FieldAlign / FieldSize so ptr/len aggregates,
            // option, and result fields contribute their
            // proper memory width.
            int[] offsets = new int[elems.Count];
            int offset = 0;
            for (int i = 0; i < elems.Count; i++)
            {
                offset = AlignTo(offset, FieldAlign(elems[i]));
                offsets[i] = offset;
                offset += FieldSize(elems[i]);
            }
            for (int i = 0; i < elems.Count; i++)
                EmitFieldLiftLocalsIfNeeded(sb, elems[i],
                    "__tup_item" + (i + 1), offsets[i]);

            sb.Append("            var __tupResult = (");
            for (int i = 0; i < elems.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FieldLiftExpression(elems[i],
                    "__tup_item" + (i + 1), offsets[i]));
            }
            sb.AppendLine(");");
            if (hasAggregate)
                EmitCabiPostInvoke(sb, ex);
            sb.AppendLine("            return __tupResult;");
        }

        // Lift a ptr/len aggregate field from a retArea offset
        // into a local. For strings → StringCoding.LiftUtf8;
        // for byte[] → BlockCopy into a fresh array. Reads
        // (ptr:i32, len:i32) at `__raw + offset`.
        private static void EmitAggregateFieldLiftLocal(
            StringBuilder sb, string fieldType,
            string localName, int offset)
        {
            sb.Append("            int ");
            sb.Append(localName);
            sb.Append("_ptr = global::Wacs.ComponentModel" +
                ".Harness.MemoryHelpers.ReadI32LE(_memory!, " +
                "__raw + ");
            sb.Append(offset);
            sb.AppendLine(");");
            sb.Append("            int ");
            sb.Append(localName);
            sb.Append("_len = global::Wacs.ComponentModel" +
                ".Harness.MemoryHelpers.ReadI32LE(_memory!, " +
                "__raw + ");
            sb.Append(offset + 4);
            sb.AppendLine(");");
            if (IsString(fieldType))
            {
                sb.Append("            string ");
                sb.Append(localName);
                sb.Append(" = global::Wacs.ComponentModel" +
                    ".Harness.StringCoding.LiftUtf8(_memory!, ");
                sb.Append(localName);
                sb.Append("_ptr, ");
                sb.Append(localName);
                sb.AppendLine("_len);");
            }
            else // primitive array
            {
                string elem = ArrayElementType(fieldType);
                int elemSize = PrimitiveSize(elem);
                sb.Append("            ");
                sb.Append(elem);
                sb.Append("[] ");
                sb.Append(localName);
                sb.Append(" = new ");
                sb.Append(elem);
                sb.Append('[');
                sb.Append(localName);
                sb.AppendLine("_len];");
                sb.Append("            if (");
                sb.Append(localName);
                sb.AppendLine("_len > 0)");
                sb.Append("                System.Buffer.BlockCopy" +
                    "(_memory!.Data, ");
                sb.Append(localName);
                sb.Append("_ptr, ");
                sb.Append(localName);
                sb.Append(", 0, ");
                sb.Append(localName);
                if (elemSize != 1)
                {
                    sb.Append("_len * ");
                    sb.Append(elemSize);
                }
                else
                {
                    sb.Append("_len");
                }
                sb.AppendLine(");");
            }
        }

        // Lazy-resolve and invoke cabi_post_<export> on __raw.
        // Some components don't emit cabi_post; absence is
        // tolerated (small per-call leak, matches wasmtime).
        private static void EmitCabiPostInvoke(
            StringBuilder sb, ExportMethod ex)
        {
            string postExport = "cabi_post_" + ex.ExportName;
            string postField = "_post_" + ex.MethodName;
            sb.Append("            if (");
            sb.Append(postField);
            sb.AppendLine(" == null)");
            sb.AppendLine("            {");
            sb.Append(
                "                if (_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"");
            sb.Append(EscapeStringLiteral(postExport));
            sb.AppendLine("\", out var __postAddr))");
            sb.Append("                    ");
            sb.Append(postField);
            sb.AppendLine(" = _instance.CoreRuntime" +
                ".CreateInvokerAction<int>(__postAddr);");
            sb.AppendLine("            }");
            sb.Append("            ");
            sb.Append(postField);
            sb.AppendLine("?.Invoke(__raw);");
        }

        // result<TOk, TErr> return lift. Two cases:
        //   * Both arms unit — flat is just the disc i32,
        //     returned directly. Build WitResult.Ok or .Err
        //     from default values.
        //   * At least one arm carries a payload — flat is a
        //     retArea i32 pointing at (disc:u8 + pad + payload).
        //     Read disc + payload, build WitResult.Ok or .Err
        //     with the right value.
        private static void EmitSyncResultReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            var (ok, err) = WitResultArgs(ex.ReturnType!);
            var joined = ResultJoinedPayload(ok, err);
            string resultType = ex.ReturnType!;

            if (joined == null)
            {
                // result<(), ()> — disc-only.
                sb.Append("            return __raw == 0 ? ");
                sb.Append(resultType);
                sb.Append(".Ok(default) : ");
                sb.Append(resultType);
                sb.AppendLine(".Err(default);");
                return;
            }

            // Non-trivial arm. __raw is the retArea pointer.
            int payloadOff = ResultPayloadOffset(ok, err);
            sb.AppendLine(
                "            byte __resDisc = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadU8(_memory!, __raw);");
            bool ptrLenJoined = IsPtrLenAggregate(joined);
            if (ptrLenJoined)
            {
                // Read the shared (ptr, len) once — both arms
                // see the same slots, the C# arm-type
                // determines whether we LiftUtf8 or LiftBytes
                // on it.
                sb.Append("            int __resPtr = global" +
                    "::Wacs.ComponentModel.Harness.MemoryHelpers" +
                    ".ReadI32LE(_memory!, __raw + ");
                sb.Append(payloadOff);
                sb.AppendLine(");");
                sb.Append("            int __resLen = global" +
                    "::Wacs.ComponentModel.Harness.MemoryHelpers" +
                    ".ReadI32LE(_memory!, __raw + ");
                sb.Append(payloadOff + 4);
                sb.AppendLine(");");
            }
            // For ptr/len joined we need the lifted values
            // before calling cabi_post (which may invalidate
            // the retArea + the underlying heap allocation).
            // Bind a single C# variable per branch, then
            // post + return.
            if (ptrLenJoined)
            {
                sb.Append("            ");
                sb.Append(resultType);
                sb.AppendLine(" __resResult;");
                sb.AppendLine("            if (__resDisc == 0)");
                sb.AppendLine("            {");
                EmitResultArmAssignLifted(sb, resultType,
                    "Ok", ok);
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
                EmitResultArmAssignLifted(sb, resultType,
                    "Err", err);
                sb.AppendLine("            }");
                EmitCabiPostInvoke(sb, ex);
                sb.AppendLine("            return __resResult;");
            }
            else
            {
                sb.AppendLine("            if (__resDisc == 0)");
                sb.AppendLine("            {");
                EmitResultArmReturn(sb,
                    resultType, "Ok", ok, joined,
                    payloadOff);
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
                EmitResultArmReturn(sb,
                    resultType, "Err", err, joined,
                    payloadOff);
                sb.AppendLine("            }");
            }
        }

        // Assign __resResult = WitResult<...>.Ok(value) /
        // .Err(value) where value is lifted from already-
        // bound __resPtr / __resLen locals, or default for
        // unit arms.
        private static void EmitResultArmAssignLifted(
            StringBuilder sb, string resultType,
            string factory, string armType)
        {
            sb.Append("                __resResult = ");
            sb.Append(resultType);
            sb.Append('.');
            sb.Append(factory);
            sb.Append('(');
            if (armType == ValueTupleType)
                sb.Append("default");
            else
                sb.Append(PtrLenLiftExpression(armType,
                    "__resPtr", "__resLen"));
            sb.AppendLine(");");
        }

        // Emit `return WitResult<...>.Ok(value)` or .Err(value)
        // where value is read from memory at the payload
        // offset, or default(T) for unit arms. For ptr/len
        // arms reads the (ptr, len) pair and lifts via
        // StringCoding / MemoryHelpers — the (ptr, len) locals
        // are bound in the caller (EmitSyncResultReturnLift).
        private static void EmitResultArmReturn(
            StringBuilder sb,
            string resultType, string factory,
            string armType, string joined, int payloadOff)
        {
            // For ptr/len joined results, prepend the
            // cabi_post invoke before each return path. The
            // retArea contents (especially the heap-allocated
            // string / byte[] body) need releasing after we
            // copy them out.
            sb.Append("                return ");
            sb.Append(resultType);
            sb.Append('.');
            sb.Append(factory);
            sb.Append('(');
            if (armType == ValueTupleType)
            {
                sb.Append("default");
            }
            else if (IsPtrLenAggregate(armType))
            {
                sb.Append(PtrLenLiftExpression(armType,
                    "__resPtr", "__resLen"));
            }
            else
            {
                sb.Append(ReadMemoryExprForType(
                    armType, "_memory!",
                    "__raw + " + payloadOff));
            }
            sb.AppendLine(");");
        }

        // option<T> return lift: __raw is the retArea pointer
        // produced by the callee. Read disc + payload at
        // canon-ABI offsets and return Nullable<T> (primitive
        // inner) or T? (string?/byte[]?). For ptr/len inner
        // the payload is (ptr, len) — lift via StringCoding
        // / LiftBytes and then invoke cabi_post.
        private static void EmitSyncOptionReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            string innerT = InnerOfNullable(ex.ReturnType!);
            int payloadOff = NullablePayloadOffset(innerT);
            sb.Append(
                "            byte __optDisc = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadU8(_memory!, __raw);");
            sb.AppendLine();

            if (IsPtrLenAggregate(innerT))
            {
                sb.AppendLine("            if (__optDisc == 0)");
                sb.AppendLine("            {");
                EmitCabiPostInvoke(sb, ex);
                sb.AppendLine("                return null;");
                sb.AppendLine("            }");
                sb.Append("            int __optPtr = global" +
                    "::Wacs.ComponentModel.Harness.MemoryHelpers" +
                    ".ReadI32LE(_memory!, __raw + ");
                sb.Append(payloadOff);
                sb.AppendLine(");");
                sb.Append("            int __optLen = global" +
                    "::Wacs.ComponentModel.Harness.MemoryHelpers" +
                    ".ReadI32LE(_memory!, __raw + ");
                sb.Append(payloadOff + 4);
                sb.AppendLine(");");
                sb.Append("            var __optResult = ");
                sb.Append(PtrLenLiftExpression(innerT,
                    "__optPtr", "__optLen"));
                sb.AppendLine(";");
                EmitCabiPostInvoke(sb, ex);
                sb.AppendLine("            return __optResult;");
                return;
            }

            sb.AppendLine("            if (__optDisc == 0) return null;");
            sb.Append("            return ");
            sb.Append(ReadMemoryExprForType(innerT, "_memory!",
                "__raw + " + payloadOff));
            sb.AppendLine(";");
        }

        // Canon-ABI alignment for primitives. Option<T>'s
        // discriminator sits at offset 0; payload starts at
        // align_to(1, alignof(T)). Now covers ptr/len inners
        // (string / byte[]) via FieldAlign which reports 4
        // for those.
        private static int NullablePayloadOffset(string innerT)
        {
            if (IsPtrLenAggregate(innerT))
                return AlignTo(1, FieldAlign(innerT));
            switch (innerT)
            {
                case "byte": case "sbyte": case "bool":
                case "System.Byte": case "System.SByte":
                case "System.Boolean":
                    return 1;
                case "short": case "ushort":
                case "System.Int16": case "System.UInt16":
                    return 2;
                case "int": case "uint": case "float":
                case "System.Int32": case "System.UInt32":
                case "System.Single":
                    return 4;
                case "long": case "ulong": case "double":
                case "System.Int64": case "System.UInt64":
                case "System.Double":
                    return 8;
                default:
                    return 4;
            }
        }

        // Generate the memory-read expression for a primitive
        // payload at a given offset.
        private static string ReadMemoryExprForType(
            string fqType, string memArg, string offArg)
        {
            string call;
            switch (fqType)
            {
                case "byte": case "System.Byte":
                    call = "ReadU8"; break;
                case "sbyte": case "System.SByte":
                    return "(sbyte)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadU8(" +
                        memArg + ", " + offArg + ")";
                case "bool": case "System.Boolean":
                    return "(global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadU8(" +
                        memArg + ", " + offArg + ") != 0)";
                case "short": case "System.Int16":
                    call = "ReadI16LE"; break;
                case "ushort": case "System.UInt16":
                    return "(ushort)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadI16LE(" +
                        memArg + ", " + offArg + ")";
                case "int": case "System.Int32":
                    call = "ReadI32LE"; break;
                case "uint": case "System.UInt32":
                    return "(uint)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadI32LE(" +
                        memArg + ", " + offArg + ")";
                case "long": case "System.Int64":
                    call = "ReadI64LE"; break;
                case "ulong": case "System.UInt64":
                    return "(ulong)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadI64LE(" +
                        memArg + ", " + offArg + ")";
                case "float": case "System.Single":
                    call = "ReadF32LE"; break;
                case "double": case "System.Double":
                    call = "ReadF64LE"; break;
                default:
                    return "default(" + fqType + ")";
            }
            return "global::Wacs.ComponentModel.Harness" +
                ".MemoryHelpers." + call + "(" + memArg +
                ", " + offArg + ")";
        }

        // Emit a `MemoryHelpers.Write*(memArg, offArg,
        // <valueExpr cast as needed>);` statement at the
        // current indentation. Handles the same primitive
        // type set as `ReadMemoryExprForType` — used by
        // list<record> lower to write each field of each
        // element at its canon-ABI memory offset.
        private static void EmitWriteMemoryStatement(
            StringBuilder sb, string fqType,
            string memArg, string offArg, string valueExpr,
            string indent)
        {
            string call;
            string cast = "";
            switch (fqType)
            {
                case "byte": case "System.Byte":
                    call = "WriteU8"; break;
                case "sbyte": case "System.SByte":
                    call = "WriteU8"; cast = "(byte)"; break;
                case "bool": case "System.Boolean":
                    // Encode bool as 0/1 byte.
                    sb.Append(indent);
                    sb.Append("global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.WriteU8(");
                    sb.Append(memArg);
                    sb.Append(", ");
                    sb.Append(offArg);
                    sb.Append(", (byte)((");
                    sb.Append(valueExpr);
                    sb.AppendLine(") ? 1 : 0));");
                    return;
                case "short": case "System.Int16":
                    call = "WriteI16LE"; break;
                case "ushort": case "System.UInt16":
                    call = "WriteI16LE"; cast = "(short)"; break;
                case "int": case "System.Int32":
                    call = "WriteI32LE"; break;
                case "uint": case "System.UInt32":
                    call = "WriteI32LE"; cast = "(int)"; break;
                case "long": case "System.Int64":
                    call = "WriteI64LE"; break;
                case "ulong": case "System.UInt64":
                    call = "WriteI64LE"; cast = "(long)"; break;
                case "float": case "System.Single":
                    call = "WriteF32LE"; break;
                case "double": case "System.Double":
                    call = "WriteF64LE"; break;
                default:
                    // Unsupported primitive — emit a no-op so
                    // builds still compile rather than crash
                    // the generator.
                    sb.Append(indent);
                    sb.Append("/* unsupported write: ");
                    sb.Append(fqType);
                    sb.AppendLine(" */");
                    return;
            }
            sb.Append(indent);
            sb.Append("global::Wacs.ComponentModel.Harness" +
                ".MemoryHelpers.");
            sb.Append(call);
            sb.Append('(');
            sb.Append(memArg);
            sb.Append(", ");
            sb.Append(offArg);
            sb.Append(", ");
            sb.Append(cast);
            sb.Append(valueExpr);
            sb.AppendLine(");");
        }

        // byte[] return lift: read (ptr, len) from retArea,
        // copy memory bytes into a fresh byte[], call
        // cabi_post_X to release.
        private static void EmitSyncPrimitiveArrayReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            string elem = ArrayElementType(ex.ReturnType!);
            int elemSize = PrimitiveSize(elem);
            sb.AppendLine(
                "            int __outPtr = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw);");
            sb.AppendLine(
                "            int __outLen = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw + 4);");
            sb.Append("            ");
            sb.Append(elem);
            sb.Append("[] __result = new ");
            sb.Append(elem);
            sb.AppendLine("[__outLen];");
            sb.AppendLine(
                "            if (__outLen > 0)");
            sb.Append(
                "                System.Buffer.BlockCopy(" +
                "_memory!.Data, __outPtr, __result, 0, __outLen");
            if (elemSize != 1)
            {
                sb.Append(" * ");
                sb.Append(elemSize);
            }
            sb.AppendLine(");");
            EmitCabiPostInvoke(sb, ex);
            sb.AppendLine("            return __result;");
        }

        // list<string> / list<record> top-level return lift.
        // Read (ptr, len) from retArea, run the per-element
        // loop into a `__result` local, then cabi_post.
        private static void EmitSyncListReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            sb.AppendLine(
                "            int __outPtr = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw);");
            sb.AppendLine(
                "            int __outLen = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw + 4);");
            EmitListLiftStatements(sb, ex.ReturnType!,
                "__result", "__outPtr", "__outLen");
            EmitCabiPostInvoke(sb, ex);
            sb.AppendLine("            return __result;");
        }

        // Memory-only ensure for option<T> returns. Same
        // lazy-resolve pattern as the full memory+realloc
        // helper, just without the realloc lookup (option<T>
        // for primitive T doesn't allocate — the callee
        // writes its retArea result inline).
        private static void EmitSyncEnsureMemoryOnly(
            StringBuilder sb)
        {
            sb.AppendLine("            if (_memory == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!_instance.CoreRuntime" +
                ".TryGetExportedMemory(\"memory\", out var __mem))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.AppendLine(
                "                        \"Aggregate return " +
                "requires an exported memory named 'memory'.\");");
            sb.AppendLine("                _memory = __mem;");
            sb.AppendLine("            }");
        }

        // String params + return all need access to the wasm
        // memory + an invoker for cabi_realloc. Emitted once
        // per call as guarded init — cheap branch on hot path.
        private static void EmitSyncEnsureMemoryAndRealloc(
            StringBuilder sb)
        {
            sb.AppendLine("            if (_memory == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!_instance.CoreRuntime" +
                ".TryGetExportedMemory(\"memory\", out var __mem))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.AppendLine(
                "                        \"String marshaling " +
                "requires an exported memory named 'memory'.\");");
            sb.AppendLine("                _memory = __mem;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (_reallocInvoke == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"cabi_realloc\", out var __reallocAddr))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.AppendLine(
                "                        \"String marshaling " +
                "requires the component to export cabi_realloc.\");");
            sb.AppendLine("                _reallocInvoke = _instance" +
                ".CoreRuntime.CreateInvokerFunc<int, int, int, int, int>" +
                "(__reallocAddr);");
            sb.AppendLine("            }");
        }

        // String return lift: memory[retArea] = ptr,
        // memory[retArea+4] = len; UTF-8 decode + cabi_post.
        private static void EmitSyncStringReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            string postExport = "cabi_post_" + ex.ExportName;
            string postField = "_post_" + ex.MethodName;
            sb.AppendLine(
                "            int __outPtr = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw);");
            sb.AppendLine(
                "            int __outLen = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw + 4);");
            sb.AppendLine(
                "            string __result = global::Wacs" +
                ".ComponentModel.Harness.StringCoding" +
                ".LiftUtf8(_memory!, __outPtr, __outLen);");
            // Lazy cabi_post resolve. Some components don't
            // emit cabi_post_X; we tolerate that by leaving
            // the retArea unfreed (small leak per call,
            // matches wasmtime's behavior when the post-return
            // function is absent).
            sb.Append("            if (");
            sb.Append(postField);
            sb.AppendLine(" == null)");
            sb.AppendLine("            {");
            sb.Append(
                "                if (_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"");
            sb.Append(EscapeStringLiteral(postExport));
            sb.AppendLine("\", out var __postAddr))");
            sb.Append("                    ");
            sb.Append(postField);
            sb.AppendLine(" = _instance.CoreRuntime" +
                ".CreateInvokerAction<int>(__postAddr);");
            sb.AppendLine("            }");
            sb.Append("            ");
            sb.Append(postField);
            sb.AppendLine("?.Invoke(__raw);");
            sb.AppendLine("            return __result;");
        }

        // Lower a single tuple-element or record-field value
        // into the flat slot locals the call site will pass.
        // For primitives: one local of the declared type.
        // For ptr/len aggregates: lower via the matching
        // helper and bind two locals — `<base>_ptr` and
        // `<base>_len`.
        private static void EmitAggregateFieldLower(
            StringBuilder sb, string fieldType,
            string baseName, string sourceExpr)
        {
            if (IsString(fieldType))
            {
                sb.Append("            global::Wacs" +
                    ".ComponentModel.Harness.StringCoding" +
                    ".LowerUtf8(_memory!, ");
                sb.Append(sourceExpr);
                sb.Append(", _reallocInvoke!, out int ");
                sb.Append(baseName);
                sb.Append("_ptr, out int ");
                sb.Append(baseName);
                sb.AppendLine("_len);");
            }
            else if (IsPrimitiveArray(fieldType))
            {
                EmitPrimitiveArrayLower(sb, fieldType,
                    baseName, sourceExpr);
            }
            else if (IsListType(fieldType))
            {
                EmitListLower(sb, fieldType,
                    baseName, sourceExpr);
            }
            else if (IsOption(fieldType))
            {
                // option<T> field — shared helper handles
                // primitive + ptr/len inner forms.
                EmitOptionArgLower(sb, fieldType,
                    baseName, sourceExpr);
            }
            else if (IsWitResult(fieldType))
            {
                // result<TOk, TErr> field — shared helper
                // handles disc + payload locals across all
                // payload shapes (primitive, ptr/len, both
                // arms unit).
                EmitResultArgLower(sb, fieldType,
                    baseName, sourceExpr);
            }
            else if (TryLookupRecord(fieldType, out var subRec))
            {
                // Nested record field: recursively lower each
                // sub-field. Naming scheme `<base>_<subField>`
                // / source `<sourceExpr>.<subField>` keeps the
                // locals unique even at arbitrary nesting.
                foreach (var f in subRec.Fields)
                    EmitAggregateFieldLower(sb, f.Type,
                        baseName + "_" + f.Name,
                        sourceExpr + "." + f.Name);
            }
            else
            {
                // Primitive: single typed local.
                sb.Append("            ");
                sb.Append(fieldType);
                sb.Append(' ');
                sb.Append(baseName);
                sb.Append(" = ");
                sb.Append(sourceExpr);
                sb.AppendLine(";");
            }
        }

        private static void EmitFlatArgsList(
            StringBuilder sb, ExportMethod ex)
        {
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                if (IsPtrLenAggregate(p.Type)
                    || IsListType(p.Type))
                {
                    sb.Append("__");
                    sb.Append(p.Name);
                    sb.Append("_ptr, __");
                    sb.Append(p.Name);
                    sb.Append("_len");
                }
                else if (IsOption(p.Type))
                {
                    string innerT = InnerOfNullable(p.Type);
                    if (IsPtrLenAggregate(innerT))
                    {
                        sb.Append("__");
                        sb.Append(p.Name);
                        sb.Append("_disc, __");
                        sb.Append(p.Name);
                        sb.Append("_ptr, __");
                        sb.Append(p.Name);
                        sb.Append("_len");
                    }
                    else
                    {
                        sb.Append("__");
                        sb.Append(p.Name);
                        sb.Append("_disc, __");
                        sb.Append(p.Name);
                        sb.Append("_payload");
                    }
                }
                else if (IsWitResult(p.Type))
                {
                    var (ok, err) = WitResultArgs(p.Type);
                    var joined = ResultJoinedPayload(ok, err);
                    sb.Append("__");
                    sb.Append(p.Name);
                    sb.Append("_disc");
                    if (joined != null)
                    {
                        sb.Append(", ");
                        if (IsPtrLenAggregate(joined))
                        {
                            sb.Append("__");
                            sb.Append(p.Name);
                            sb.Append("_ptr, __");
                            sb.Append(p.Name);
                            sb.Append("_len");
                        }
                        else
                        {
                            sb.Append("__");
                            sb.Append(p.Name);
                            sb.Append("_payload");
                        }
                    }
                }
                else if (IsTuple(p.Type))
                {
                    var elems = TupleElementTypes(p.Type);
                    bool firstSlot = true;
                    for (int i = 0; i < elems.Count; i++)
                    {
                        string baseName = "__" + p.Name
                            + "_item" + (i + 1);
                        if (!firstSlot) sb.Append(", ");
                        EmitFlatArgsForField(sb, elems[i],
                            baseName);
                        firstSlot = false;
                    }
                }
                else if (TryLookupRecord(p.Type, out var rec))
                {
                    bool firstSlot = true;
                    foreach (var f in rec.Fields)
                    {
                        string baseName = "__" + p.Name
                            + "_" + f.Name;
                        if (!firstSlot) sb.Append(", ");
                        EmitFlatArgsForField(sb, f.Type,
                            baseName);
                        firstSlot = false;
                    }
                }
                else
                {
                    sb.Append(p.Name);
                }
            }
        }

        // Emit statements that lower a string / primitive
        // array value into existing ptr / len locals. `out`
        // style for string (LowerUtf8 supports `out` to
        // existing locals); plain assignments for arrays.
        // `indent` is the leading whitespace for each
        // emitted line.
        private static void EmitPtrLenAssignInto(
            StringBuilder sb, string type, string sourceExpr,
            string ptrLocal, string lenLocal, string indent)
        {
            if (IsString(type))
            {
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.StringCoding.LowerUtf8(_memory!, ");
                sb.Append(sourceExpr);
                sb.Append(", _reallocInvoke!, out ");
                sb.Append(ptrLocal);
                sb.Append(", out ");
                sb.Append(lenLocal);
                sb.AppendLine(");");
                return;
            }
            // Primitive arm in a mixed-shape result (e.g.,
            // `result<int, string>`): pack the value into
            // slot 1 (`ptrLocal`); slot 2 stays at 0. Cast
            // depends on the primitive's natural C# type —
            // bool widens to int via `? 1 : 0`; float
            // re-bits via SingleToInt32Bits; the rest cast
            // to int directly.
            if (IsPrimitive(type))
            {
                sb.Append(indent);
                sb.Append(ptrLocal);
                sb.Append(" = ");
                switch (type)
                {
                    case "bool":
                    case "System.Boolean":
                        sb.Append("(");
                        sb.Append(sourceExpr);
                        sb.Append(" ? 1 : 0)");
                        break;
                    case "float":
                    case "System.Single":
                        sb.Append("global::System.BitConverter" +
                            ".SingleToInt32Bits(");
                        sb.Append(sourceExpr);
                        sb.Append(")");
                        break;
                    default:
                        sb.Append("(int)(");
                        sb.Append(sourceExpr);
                        sb.Append(")");
                        break;
                }
                sb.AppendLine(";");
                return;
            }
            // List type (string[] / T[][]): run the full list
            // lower into a fresh `<inner>_ptr` / `<inner>_len`
            // pair, then copy those values to the target
            // locals. The counter-suffixed base ensures
            // multiple list lowerings in the same outer
            // scope don't collide on the inner-list locals
            // (the for-loop vars themselves are block-scoped
            // by C# so they stay safe).
            if (IsListType(type))
            {
                int n = NextListInnerId();
                string innerBase = "__listInner" + n;
                EmitListLower(sb, type, innerBase, sourceExpr);
                sb.Append(indent);
                sb.Append(ptrLocal);
                sb.Append(" = ");
                sb.Append(innerBase);
                sb.AppendLine("_ptr;");
                sb.Append(indent);
                sb.Append(lenLocal);
                sb.Append(" = ");
                sb.Append(innerBase);
                sb.AppendLine("_len;");
                return;
            }
            // Primitive array: scale realloc size + BlockCopy
            // count by sizeof(element). For byte[] the scale
            // is 1 so the generated source matches the old
            // byte[]-specific path byte-for-byte.
            string elem = ArrayElementType(type);
            int elemSize = PrimitiveSize(elem);
            int elemAlign = PrimitiveAlign(elem);
            sb.Append(indent);
            sb.Append(lenLocal);
            sb.Append(" = ");
            sb.Append(sourceExpr);
            sb.AppendLine(".Length;");
            sb.Append(indent);
            sb.Append(ptrLocal);
            sb.Append(" = ");
            sb.Append(lenLocal);
            sb.Append(" == 0 ? 0 : _reallocInvoke!(0, 0, ");
            sb.Append(elemAlign);
            sb.Append(", ");
            sb.Append(lenLocal);
            if (elemSize != 1)
            {
                sb.Append(" * ");
                sb.Append(elemSize);
            }
            sb.AppendLine(");");
            sb.Append(indent);
            sb.Append("if (");
            sb.Append(lenLocal);
            sb.Append(" > 0) System.Buffer.BlockCopy(");
            sb.Append(sourceExpr);
            sb.Append(", 0, _memory!.Data, ");
            sb.Append(ptrLocal);
            sb.Append(", ");
            sb.Append(lenLocal);
            if (elemSize != 1)
            {
                sb.Append(" * ");
                sb.Append(elemSize);
            }
            sb.AppendLine(");");
        }

        // Top-level primitive-array param lower. Wraps
        // EmitPtrLenAssignInto with the inline-int-declaration
        // form for ptr/len locals + the "realloc returned 0"
        // safety check.
        private static void EmitPrimitiveArrayLower(
            StringBuilder sb, string type,
            string baseName, string sourceExpr)
        {
            string elem = ArrayElementType(type);
            int elemSize = PrimitiveSize(elem);
            int elemAlign = PrimitiveAlign(elem);
            sb.Append("            int ");
            sb.Append(baseName);
            sb.Append("_len = ");
            sb.Append(sourceExpr);
            sb.AppendLine(".Length;");
            sb.Append("            int ");
            sb.Append(baseName);
            sb.Append("_ptr = ");
            sb.Append(baseName);
            sb.Append("_len == 0 ? 0 : _reallocInvoke!(0, 0, ");
            sb.Append(elemAlign);
            sb.Append(", ");
            sb.Append(baseName);
            if (elemSize != 1)
            {
                sb.Append("_len * ");
                sb.Append(elemSize);
            }
            else
            {
                sb.Append("_len");
            }
            sb.AppendLine(");");
            sb.Append("            if (");
            sb.Append(baseName);
            sb.Append("_ptr == 0 && ");
            sb.Append(baseName);
            sb.AppendLine("_len != 0)");
            sb.AppendLine("                throw new " +
                "System.OutOfMemoryException(");
            sb.Append("                    \"cabi_realloc " +
                "returned 0 when lowering ");
            sb.Append(type);
            sb.AppendLine(" param.\");");
            sb.Append("            if (");
            sb.Append(baseName);
            sb.Append("_len > 0) System.Buffer.BlockCopy(");
            sb.Append(sourceExpr);
            sb.Append(", 0, _memory!.Data, ");
            sb.Append(baseName);
            sb.Append("_ptr, ");
            sb.Append(baseName);
            if (elemSize != 1)
            {
                sb.Append("_len * ");
                sb.Append(elemSize);
            }
            else
            {
                sb.Append("_len");
            }
            sb.AppendLine(");");
        }

        // list<string> / list<record> lower: allocate the
        // outer array, then per-element write the element
        // body into wasm memory at the element's canon-ABI
        // memory offset (i * FieldSize(elem)).
        //
        // Naming convention: the outer (ptr, len) locals are
        // `<baseName>_ptr` / `<baseName>_len`. Per-element
        // locals (offset, element-ptr/len, etc.) are scoped
        // inside the for-loop block — safe to reuse short
        // names across multiple list lowerings.
        private static void EmitListLower(
            StringBuilder sb, string listType,
            string baseName, string sourceExpr,
            int depth = 0)
        {
            string elem = ArrayElementType(listType);
            int elemSize = FieldSize(elem);
            int elemAlign = FieldAlign(elem);
            string iVar = depth == 0
                ? "__i" : "__i" + depth;
            string offVar = depth == 0
                ? "__elemOff" : "__elemOff" + depth;
            sb.Append("            int ");
            sb.Append(baseName);
            sb.Append("_len = ");
            sb.Append(sourceExpr);
            sb.AppendLine(".Length;");
            sb.Append("            int ");
            sb.Append(baseName);
            sb.Append("_ptr = ");
            sb.Append(baseName);
            sb.Append("_len == 0 ? 0 : _reallocInvoke!(0, 0, ");
            sb.Append(elemAlign);
            sb.Append(", ");
            sb.Append(baseName);
            sb.Append("_len * ");
            sb.Append(elemSize);
            sb.AppendLine(");");
            sb.Append("            for (int ");
            sb.Append(iVar);
            sb.Append(" = 0; ");
            sb.Append(iVar);
            sb.Append(" < ");
            sb.Append(baseName);
            sb.Append("_len; ");
            sb.Append(iVar);
            sb.AppendLine("++)");
            sb.AppendLine("            {");
            sb.Append("                int ");
            sb.Append(offVar);
            sb.Append(" = ");
            sb.Append(baseName);
            sb.Append("_ptr + ");
            sb.Append(iVar);
            sb.Append(" * ");
            sb.Append(elemSize);
            sb.AppendLine(";");
            EmitListElementLower(sb, elem,
                sourceExpr + "[" + iVar + "]",
                offVar, depth);
            sb.AppendLine("            }");
        }

        // Write one list element at the given memory offset.
        // - string element: lower via StringCoding.LowerUtf8
        //   into local (ptr, len) and write at +0 / +4.
        // - record element (primitive-only fields): write
        //   each field at its canon-ABI offset within the
        //   element using EmitWriteMemoryStatement.
        private static void EmitListElementLower(
            StringBuilder sb, string elemType,
            string valueExpr, string offsetExpr,
            int depth = 0)
        {
            if (IsString(elemType))
            {
                // String element: lower body, write (ptr, len)
                // at the element's offset. Use depth-suffixed
                // locals so nested lowerings don't collide.
                string ptrLoc = "__elemPtr"
                    + (depth == 0 ? "" : depth.ToString());
                string lenLoc = "__elemLen"
                    + (depth == 0 ? "" : depth.ToString());
                sb.Append("                global::Wacs" +
                    ".ComponentModel.Harness.StringCoding" +
                    ".LowerUtf8(_memory!, ");
                sb.Append(valueExpr);
                sb.Append(", _reallocInvoke!, out int ");
                sb.Append(ptrLoc);
                sb.Append(", out int ");
                sb.Append(lenLoc);
                sb.AppendLine(");");
                sb.Append("                global::Wacs" +
                    ".ComponentModel.Harness.MemoryHelpers" +
                    ".WriteI32LE(_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(", ");
                sb.Append(ptrLoc);
                sb.AppendLine(");");
                sb.Append("                global::Wacs" +
                    ".ComponentModel.Harness.MemoryHelpers" +
                    ".WriteI32LE(_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(" + 4, ");
                sb.Append(lenLoc);
                sb.AppendLine(");");
                return;
            }
            // Nested list / primitive array element: invoke
            // the inner list lower with a depth-bumped base.
            // After return, the inner `<innerBase>_ptr` /
            // `<innerBase>_len` locals are bound — write them
            // at the parent element's offset.
            if (IsPrimitiveArray(elemType)
                || IsListType(elemType))
            {
                string innerBase = "__inner" + (depth + 1);
                if (IsPrimitiveArray(elemType))
                {
                    EmitPrimitiveArrayLower(sb, elemType,
                        innerBase, valueExpr);
                }
                else
                {
                    EmitListLower(sb, elemType,
                        innerBase, valueExpr, depth + 1);
                }
                sb.Append("                global::Wacs" +
                    ".ComponentModel.Harness.MemoryHelpers" +
                    ".WriteI32LE(_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(", ");
                sb.Append(innerBase);
                sb.AppendLine("_ptr);");
                sb.Append("                global::Wacs" +
                    ".ComponentModel.Harness.MemoryHelpers" +
                    ".WriteI32LE(_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(" + 4, ");
                sb.Append(innerBase);
                sb.AppendLine("_len);");
                return;
            }
            if (TryLookupRecord(elemType, out var rec))
            {
                foreach (var f in rec.Fields)
                {
                    if (IsPtrLenAggregate(f.Type))
                    {
                        // Lower the field body (string body,
                        // byte[] body, primitive-array body)
                        // and write the (ptr, len) pair at
                        // the field's canon-ABI offset within
                        // the element. Locals are field-name
                        // suffixed so multiple ptr/len fields
                        // don't collide in the for-body.
                        string ptrLoc = "__f_" + f.Name
                            + "_ptr";
                        string lenLoc = "__f_" + f.Name
                            + "_len";
                        sb.Append("                int ");
                        sb.Append(ptrLoc);
                        sb.AppendLine(" = 0;");
                        sb.Append("                int ");
                        sb.Append(lenLoc);
                        sb.AppendLine(" = 0;");
                        EmitPtrLenAssignInto(sb, f.Type,
                            valueExpr + "." + f.Name,
                            ptrLoc, lenLoc,
                            "                ");
                        sb.Append("                global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.WriteI32LE(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset);
                        sb.Append(", ");
                        sb.Append(ptrLoc);
                        sb.AppendLine(");");
                        sb.Append("                global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.WriteI32LE(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset + 4);
                        sb.Append(", ");
                        sb.Append(lenLoc);
                        sb.AppendLine(");");
                    }
                    else if (IsNullablePrimitive(f.Type))
                    {
                        // option<primitive> field: disc:u8 at
                        // f.Offset, payload at f.Offset +
                        // NullablePayloadOffset(inner).
                        string innerT = InnerOfNullable(f.Type);
                        int payOff = NullablePayloadOffset(
                            innerT);
                        sb.Append("                global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.WriteU8(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset);
                        sb.Append(", (byte)(");
                        sb.Append(valueExpr);
                        sb.Append('.');
                        sb.Append(f.Name);
                        sb.AppendLine(".HasValue ? 1 : 0));");
                        EmitWriteMemoryStatement(sb, innerT,
                            "_memory!",
                            offsetExpr + " + "
                                + (f.Offset + payOff),
                            valueExpr + "." + f.Name
                                + ".GetValueOrDefault()",
                            "                ");
                    }
                    else if (IsOptionRef(f.Type))
                    {
                        // option<ptr/len> field: disc:u8 at
                        // f.Offset; if !=null lower body into
                        // (ptr, len) locals and write at
                        // f.Offset + payOff and +payOff+4.
                        // Both slots zero if null.
                        string innerT = InnerOfNullable(f.Type);
                        int payOff = NullablePayloadOffset(
                            innerT);
                        string ptrLoc = "__f_" + f.Name
                            + "_ptr";
                        string lenLoc = "__f_" + f.Name
                            + "_len";
                        sb.Append("                global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.WriteU8(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset);
                        sb.Append(", (byte)(");
                        sb.Append(valueExpr);
                        sb.Append('.');
                        sb.Append(f.Name);
                        sb.AppendLine(" != null ? 1 : 0));");
                        sb.Append("                int ");
                        sb.Append(ptrLoc);
                        sb.AppendLine(" = 0;");
                        sb.Append("                int ");
                        sb.Append(lenLoc);
                        sb.AppendLine(" = 0;");
                        sb.Append("                if (");
                        sb.Append(valueExpr);
                        sb.Append('.');
                        sb.Append(f.Name);
                        sb.AppendLine(" != null)");
                        sb.AppendLine("                {");
                        EmitPtrLenAssignInto(sb, innerT,
                            valueExpr + "." + f.Name,
                            ptrLoc, lenLoc,
                            "                    ");
                        sb.AppendLine("                }");
                        sb.Append("                global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.WriteI32LE(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset + payOff);
                        sb.Append(", ");
                        sb.Append(ptrLoc);
                        sb.AppendLine(");");
                        sb.Append("                global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.WriteI32LE(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset + payOff + 4);
                        sb.Append(", ");
                        sb.Append(lenLoc);
                        sb.AppendLine(");");
                    }
                    else if (TryLookupRecord(f.Type, out _)
                        || IsWitResult(f.Type))
                    {
                        // Nested record / result field —
                        // recursive lower via the shared
                        // helper.
                        EmitInlineRecordFieldLowerStatements(
                            sb, f.Type,
                            valueExpr + "." + f.Name,
                            offsetExpr + " + " + f.Offset,
                            "                ");
                    }
                    else
                    {
                        EmitWriteMemoryStatement(sb, f.Type,
                            "_memory!",
                            offsetExpr + " + " + f.Offset,
                            valueExpr + "." + f.Name,
                            "                ");
                    }
                }
            }
        }

        // list<string> / list<record> lift: read each element
        // at i * FieldSize(elem), decode, store into a
        // freshly-allocated C# array. Binds a local of name
        // `<resultLocal>` holding the lifted array. Caller
        // is responsible for the cabi_post call.
        private static void EmitListLiftStatements(
            StringBuilder sb, string listType,
            string resultLocal,
            string ptrLocal, string lenLocal,
            int depth = 0)
        {
            string elem = ArrayElementType(listType);
            int elemSize = FieldSize(elem);
            string iVar = depth == 0
                ? "__i" : "__i" + depth;
            string offVar = depth == 0
                ? "__elemOff" : "__elemOff" + depth;
            // C# jagged-array allocation: `new T[size]` for
            // T with no further []s, `new Base[size][]...[]`
            // when T itself is an array type. Use the helper
            // so list-of-list lifts emit valid syntax.
            sb.Append("            ");
            sb.Append(elem);
            sb.Append("[] ");
            sb.Append(resultLocal);
            sb.Append(" = ");
            sb.Append(JaggedArrayAllocExpression(elem,
                lenLocal));
            sb.AppendLine(";");
            sb.Append("            for (int ");
            sb.Append(iVar);
            sb.Append(" = 0; ");
            sb.Append(iVar);
            sb.Append(" < ");
            sb.Append(lenLocal);
            sb.Append("; ");
            sb.Append(iVar);
            sb.AppendLine("++)");
            sb.AppendLine("            {");
            sb.Append("                int ");
            sb.Append(offVar);
            sb.Append(" = ");
            sb.Append(ptrLocal);
            sb.Append(" + ");
            sb.Append(iVar);
            sb.Append(" * ");
            sb.Append(elemSize);
            sb.AppendLine(";");
            EmitListElementLift(sb, elem,
                resultLocal + "[" + iVar + "]",
                offVar, depth);
            sb.AppendLine("            }");
        }

        // Read one list element at the given memory offset
        // and assign into `targetExpr`.
        // - string element: read (ptr, len), LiftUtf8.
        // - record element (primitive-only fields): read
        //   each field and construct via property
        //   initializer.
        private static void EmitListElementLift(
            StringBuilder sb, string elemType,
            string targetExpr, string offsetExpr,
            int depth = 0)
        {
            if (IsString(elemType))
            {
                // Use the inline ReadI32LE form so we don't
                // need to declare locals — the assignment
                // happens in one statement.
                sb.Append("                ");
                sb.Append(targetExpr);
                sb.Append(" = ");
                sb.Append(PtrLenLiftExpressionAtOffset(
                    elemType, offsetExpr));
                sb.AppendLine(";");
                return;
            }
            // Primitive-array / nested-list element: read
            // inner (ptr, len), then lift via the matching
            // primitive-array helper or recursive list-lift.
            if (IsPtrLenAggregate(elemType))
            {
                // byte[] / int[] / etc — single-expression lift.
                sb.Append("                ");
                sb.Append(targetExpr);
                sb.Append(" = ");
                sb.Append(PtrLenLiftExpressionAtOffset(
                    elemType, offsetExpr));
                sb.AppendLine(";");
                return;
            }
            if (IsListType(elemType))
            {
                string innerLocal = "__inner" + (depth + 1);
                sb.Append("                int ");
                sb.Append(innerLocal);
                sb.Append("_ptr = global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.ReadI32LE(_memory!, ");
                sb.Append(offsetExpr);
                sb.AppendLine(");");
                sb.Append("                int ");
                sb.Append(innerLocal);
                sb.Append("_len = global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.ReadI32LE(_memory!, ");
                sb.Append(offsetExpr);
                sb.AppendLine(" + 4);");
                EmitListLiftStatements(sb, elemType,
                    innerLocal,
                    innerLocal + "_ptr",
                    innerLocal + "_len",
                    depth + 1);
                sb.Append("                ");
                sb.Append(targetExpr);
                sb.Append(" = ");
                sb.Append(innerLocal);
                sb.AppendLine(";");
                return;
            }
            if (TryLookupRecord(elemType, out var rec))
            {
                // Property-initializer with one expression
                // per field. Primitive fields use the inline
                // read; ptr/len-aggregate fields (string /
                // byte[] / primitive arrays) use the shared
                // `PtrLenLiftExpressionAtOffset` so the
                // record construction stays a single
                // expression.
                sb.Append("                ");
                sb.Append(targetExpr);
                sb.Append(" = new ");
                sb.Append(elemType);
                sb.Append(" { ");
                bool first = true;
                foreach (var f in rec.Fields)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(f.Name);
                    sb.Append(" = ");
                    if (IsPtrLenAggregate(f.Type))
                    {
                        sb.Append(
                            PtrLenLiftExpressionAtOffset(
                                f.Type,
                                offsetExpr + " + "
                                    + f.Offset));
                    }
                    else if (IsNullablePrimitive(f.Type))
                    {
                        // option<primitive> inline lift:
                        // (disc != 0 ? payload : null).
                        string innerT = InnerOfNullable(
                            f.Type);
                        int payOff = NullablePayloadOffset(
                            innerT);
                        sb.Append("(global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.ReadU8(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset);
                        sb.Append(") == 0 ? (");
                        sb.Append(f.Type);
                        sb.Append(")null : ");
                        sb.Append(ReadMemoryExprForType(innerT,
                            "_memory!",
                            offsetExpr + " + "
                                + (f.Offset + payOff)));
                        sb.Append(")");
                    }
                    else if (IsOptionRef(f.Type))
                    {
                        // option<ptr/len> inline lift:
                        // (disc == 0 ? null :
                        //   LiftXxx(_memory!,
                        //     ReadI32(off+payOff),
                        //     ReadI32(off+payOff+4))).
                        string innerT = InnerOfNullable(
                            f.Type);
                        int payOff = NullablePayloadOffset(
                            innerT);
                        sb.Append("(global::Wacs" +
                            ".ComponentModel.Harness" +
                            ".MemoryHelpers.ReadU8(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(f.Offset);
                        sb.Append(") == 0 ? null : ");
                        sb.Append(PtrLenLiftExpressionAtOffset(
                            innerT,
                            offsetExpr + " + "
                                + (f.Offset + payOff)));
                        sb.Append(")");
                    }
                    else if (TryLookupRecord(f.Type, out _)
                        || IsWitResult(f.Type))
                    {
                        // Nested record / result field —
                        // recursive lift via the shared
                        // helper.
                        sb.Append(
                            InlineRecordFieldLiftExpression(
                                f.Type,
                                offsetExpr + " + "
                                    + f.Offset));
                    }
                    else
                    {
                        sb.Append(ReadMemoryExprForType(f.Type,
                            "_memory!",
                            offsetExpr + " + " + f.Offset));
                    }
                    first = false;
                }
                sb.AppendLine(" };");
            }
        }

        // C# jagged-array allocation for an outer array of
        // `elemType[size]`. If elemType has no [] suffix, the
        // result is `new <elemType>[size]`. For nested array
        // element types (e.g., `string[]`), C# requires the
        // size in the outer pair and the inner brackets to
        // trail: `new string[size][]`. Recurses through any
        // depth of jagging.
        private static string JaggedArrayAllocExpression(
            string elemType, string sizeExpr)
        {
            int innerDims = 0;
            string baseT = elemType;
            while (baseT.EndsWith("[]",
                StringComparison.Ordinal))
            {
                innerDims++;
                baseT = baseT.Substring(0,
                    baseT.Length - 2);
            }
            var s = new StringBuilder();
            s.Append("new ");
            s.Append(baseT);
            s.Append('[');
            s.Append(sizeExpr);
            s.Append(']');
            for (int i = 0; i < innerDims; i++)
                s.Append("[]");
            return s.ToString();
        }

        // Recursive multi-statement lower for a record /
        // tuple field at `offsetExpr`. Mirrors
        // InlineRecordFieldLiftExpression: handles primitive
        // (WriteX inline), ptr/len (counter-suffixed locals
        // + WriteI32LE), option<primitive>
        // (WriteU8 + WriteX), option<ptr/len> (WriteU8 +
        // conditional lower + WriteI32LE pair), and nested
        // record (recurse with offset bumped by sub.Offset).
        private static void EmitInlineRecordFieldLowerStatements(
            StringBuilder sb, string fieldType,
            string valueExpr, string offsetExpr,
            string indent)
        {
            if (IsPtrLenAggregate(fieldType))
            {
                int n = NextListInnerId();
                string ptrLoc = "__rfPtr" + n;
                string lenLoc = "__rfLen" + n;
                sb.Append(indent);
                sb.Append("int ");
                sb.Append(ptrLoc);
                sb.AppendLine(" = 0;");
                sb.Append(indent);
                sb.Append("int ");
                sb.Append(lenLoc);
                sb.AppendLine(" = 0;");
                EmitPtrLenAssignInto(sb, fieldType,
                    valueExpr, ptrLoc, lenLoc, indent);
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteI32LE(" +
                    "_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(", ");
                sb.Append(ptrLoc);
                sb.AppendLine(");");
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteI32LE(" +
                    "_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(" + 4, ");
                sb.Append(lenLoc);
                sb.AppendLine(");");
                return;
            }
            if (IsNullablePrimitive(fieldType))
            {
                string innerT = InnerOfNullable(fieldType);
                int payOff = NullablePayloadOffset(innerT);
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteU8(_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(", (byte)(");
                sb.Append(valueExpr);
                sb.AppendLine(".HasValue ? 1 : 0));");
                EmitWriteMemoryStatement(sb, innerT,
                    "_memory!",
                    offsetExpr + " + " + payOff,
                    valueExpr + ".GetValueOrDefault()",
                    indent);
                return;
            }
            if (IsOptionRef(fieldType))
            {
                string innerT = InnerOfNullable(fieldType);
                int payOff = NullablePayloadOffset(innerT);
                int n = NextListInnerId();
                string ptrLoc = "__rfPtr" + n;
                string lenLoc = "__rfLen" + n;
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteU8(_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(", (byte)(");
                sb.Append(valueExpr);
                sb.AppendLine(" != null ? 1 : 0));");
                sb.Append(indent);
                sb.Append("int ");
                sb.Append(ptrLoc);
                sb.AppendLine(" = 0;");
                sb.Append(indent);
                sb.Append("int ");
                sb.Append(lenLoc);
                sb.AppendLine(" = 0;");
                sb.Append(indent);
                sb.Append("if (");
                sb.Append(valueExpr);
                sb.AppendLine(" != null)");
                sb.Append(indent);
                sb.AppendLine("{");
                EmitPtrLenAssignInto(sb, innerT, valueExpr,
                    ptrLoc, lenLoc, indent + "    ");
                sb.Append(indent);
                sb.AppendLine("}");
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteI32LE(" +
                    "_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(" + ");
                sb.Append(payOff);
                sb.Append(", ");
                sb.Append(ptrLoc);
                sb.AppendLine(");");
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteI32LE(" +
                    "_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(" + ");
                sb.Append(payOff + 4);
                sb.Append(", ");
                sb.Append(lenLoc);
                sb.AppendLine(");");
                return;
            }
            if (TryLookupRecord(fieldType, out var sub))
            {
                foreach (var f in sub.Fields)
                {
                    EmitInlineRecordFieldLowerStatements(sb,
                        f.Type,
                        valueExpr + "." + f.Name,
                        offsetExpr + " + " + f.Offset,
                        indent);
                }
                return;
            }
            if (IsWitResult(fieldType))
            {
                // Write disc at field offset; write payload
                // at field offset + ResultPayloadOffset based
                // on the active arm. Mirrors
                // EmitResultArgLower but writes to memory at
                // the field offset rather than to flat slot
                // locals.
                var (ok, err) = WitResultArgs(fieldType);
                var joined = ResultJoinedPayload(ok, err);
                int payOff = ResultPayloadOffset(ok, err);
                sb.Append(indent);
                sb.Append("global::Wacs.ComponentModel" +
                    ".Harness.MemoryHelpers.WriteU8(" +
                    "_memory!, ");
                sb.Append(offsetExpr);
                sb.Append(", (byte)(");
                sb.Append(valueExpr);
                sb.AppendLine(".IsOk ? 0 : 1));");
                if (joined != null)
                {
                    if (IsPtrLenAggregate(joined))
                    {
                        // ptr/len payload — branch on .IsOk
                        // and write (ptr, len) via a local
                        // pair.
                        int n = NextListInnerId();
                        string ptrLoc = "__rfRPtr" + n;
                        string lenLoc = "__rfRLen" + n;
                        sb.Append(indent);
                        sb.Append("int ");
                        sb.Append(ptrLoc);
                        sb.AppendLine(" = 0;");
                        sb.Append(indent);
                        sb.Append("int ");
                        sb.Append(lenLoc);
                        sb.AppendLine(" = 0;");
                        sb.Append(indent);
                        sb.Append("if (");
                        sb.Append(valueExpr);
                        sb.AppendLine(".IsOk)");
                        sb.Append(indent);
                        sb.AppendLine("{");
                        if (ok != ValueTupleType)
                            EmitPtrLenAssignInto(sb, ok,
                                valueExpr + ".OkValue!",
                                ptrLoc, lenLoc,
                                indent + "    ");
                        sb.Append(indent);
                        sb.AppendLine("}");
                        sb.Append(indent);
                        sb.AppendLine("else");
                        sb.Append(indent);
                        sb.AppendLine("{");
                        if (err != ValueTupleType)
                            EmitPtrLenAssignInto(sb, err,
                                valueExpr + ".ErrValue!",
                                ptrLoc, lenLoc,
                                indent + "    ");
                        sb.Append(indent);
                        sb.AppendLine("}");
                        sb.Append(indent);
                        sb.Append("global::Wacs.ComponentModel" +
                            ".Harness.MemoryHelpers" +
                            ".WriteI32LE(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(payOff);
                        sb.Append(", ");
                        sb.Append(ptrLoc);
                        sb.AppendLine(");");
                        sb.Append(indent);
                        sb.Append("global::Wacs.ComponentModel" +
                            ".Harness.MemoryHelpers" +
                            ".WriteI32LE(_memory!, ");
                        sb.Append(offsetExpr);
                        sb.Append(" + ");
                        sb.Append(payOff + 4);
                        sb.Append(", ");
                        sb.Append(lenLoc);
                        sb.AppendLine(");");
                    }
                    else
                    {
                        // Primitive joined payload — write
                        // the appropriate arm's value at
                        // payOff. Inactive arm contributes
                        // default(T) (the wasm side reads
                        // only the arm matching disc).
                        string val =
                            valueExpr + ".IsOk ? "
                            + (ok == ValueTupleType
                                ? "default(" + joined + ")"
                                : valueExpr + ".OkValue")
                            + " : "
                            + (err == ValueTupleType
                                ? "default(" + joined + ")"
                                : valueExpr + ".ErrValue");
                        EmitWriteMemoryStatement(sb, joined,
                            "_memory!",
                            offsetExpr + " + " + payOff,
                            "(" + val + ")", indent);
                    }
                }
                return;
            }
            // Primitive — falls through to a simple Write.
            EmitWriteMemoryStatement(sb, fieldType,
                "_memory!", offsetExpr, valueExpr, indent);
        }

        // Recursive single-expression lift for a record /
        // tuple field at `offsetExpr` within memory. Used by
        // the list<record> element lift to build inline
        // property-initializers across primitive / ptr/len /
        // option / nested-record field types. Multi-statement
        // shapes (result, list-of-record nested via list-of-
        // X) still need the field-by-field helpers, but
        // anything that lifts to a single expression composes
        // through here.
        private static string InlineRecordFieldLiftExpression(
            string fieldType, string offsetExpr)
        {
            if (IsPtrLenAggregate(fieldType))
                return PtrLenLiftExpressionAtOffset(
                    fieldType, offsetExpr);
            if (IsNullablePrimitive(fieldType))
            {
                string innerT = InnerOfNullable(fieldType);
                int payOff = NullablePayloadOffset(innerT);
                return "(global::Wacs.ComponentModel.Harness" +
                    ".MemoryHelpers.ReadU8(_memory!, "
                    + offsetExpr + ") == 0 ? ("
                    + fieldType + ")null : "
                    + ReadMemoryExprForType(innerT,
                        "_memory!",
                        offsetExpr + " + " + payOff)
                    + ")";
            }
            if (IsOptionRef(fieldType))
            {
                string innerT = InnerOfNullable(fieldType);
                int payOff = NullablePayloadOffset(innerT);
                return "(global::Wacs.ComponentModel.Harness" +
                    ".MemoryHelpers.ReadU8(_memory!, "
                    + offsetExpr + ") == 0 ? null : "
                    + PtrLenLiftExpressionAtOffset(innerT,
                        offsetExpr + " + " + payOff)
                    + ")";
            }
            if (TryLookupRecord(fieldType, out var sub))
            {
                var s = new StringBuilder();
                s.Append("new ");
                s.Append(fieldType);
                s.Append(" { ");
                bool first = true;
                foreach (var f in sub.Fields)
                {
                    if (!first) s.Append(", ");
                    s.Append(f.Name);
                    s.Append(" = ");
                    s.Append(InlineRecordFieldLiftExpression(
                        f.Type,
                        offsetExpr + " + " + f.Offset));
                    first = false;
                }
                s.Append(" }");
                return s.ToString();
            }
            if (IsWitResult(fieldType))
            {
                // Single-expression result lift:
                //   (disc == 0 ? T.Ok(<okExpr>) :
                //                T.Err(<errExpr>))
                // where <okExpr>/<errExpr> read the joined
                // payload slots inline at the field's offset.
                var (ok, err) = WitResultArgs(fieldType);
                var joined = ResultJoinedPayload(ok, err);
                int payOff = ResultPayloadOffset(ok, err);
                string discRead =
                    "global::Wacs.ComponentModel.Harness" +
                    ".MemoryHelpers.ReadU8(_memory!, "
                    + offsetExpr + ")";
                string okBody, errBody;
                if (joined == null || ok == ValueTupleType)
                    okBody = "default";
                else if (IsPtrLenAggregate(ok))
                    okBody = PtrLenLiftExpressionAtOffset(
                        ok, offsetExpr + " + " + payOff);
                else
                    okBody = ReadMemoryExprForType(ok,
                        "_memory!",
                        offsetExpr + " + " + payOff);
                if (joined == null || err == ValueTupleType)
                    errBody = "default";
                else if (IsPtrLenAggregate(err))
                    errBody = PtrLenLiftExpressionAtOffset(
                        err, offsetExpr + " + " + payOff);
                else
                    errBody = ReadMemoryExprForType(err,
                        "_memory!",
                        offsetExpr + " + " + payOff);
                return "(" + discRead + " == 0 ? "
                    + fieldType + ".Ok(" + okBody + ") : "
                    + fieldType + ".Err(" + errBody + "))";
            }
            return ReadMemoryExprForType(fieldType,
                "_memory!", offsetExpr);
        }

        // PtrLenLiftExpression variant that reads the
        // (ptr, len) inline at `offsetExpr` / `offsetExpr + 4`
        // rather than from named locals. Used inside property-
        // initializers where binding intermediate locals would
        // require an outer block.
        private static string PtrLenLiftExpressionAtOffset(
            string type, string offsetExpr)
        {
            string ptrExpr =
                "global::Wacs.ComponentModel.Harness" +
                ".MemoryHelpers.ReadI32LE(_memory!, "
                + offsetExpr + ")";
            string lenExpr =
                "global::Wacs.ComponentModel.Harness" +
                ".MemoryHelpers.ReadI32LE(_memory!, "
                + offsetExpr + " + 4)";
            return PtrLenLiftExpression(type,
                ptrExpr, lenExpr);
        }

        // C# expression that lifts a string / primitive
        // array from existing ptr / len locals. Single
        // expression form — string uses
        // StringCoding.LiftUtf8, byte[] uses
        // MemoryHelpers.LiftBytes (kept for backward
        // compat), other primitive arrays use the generic
        // MemoryHelpers.LiftPrimitiveArray<T> overload.
        private static string PtrLenLiftExpression(
            string type, string ptrLocal, string lenLocal)
        {
            if (IsString(type))
                return "global::Wacs.ComponentModel.Harness" +
                    ".StringCoding.LiftUtf8(_memory!, "
                    + ptrLocal + ", " + lenLocal + ")";
            if (IsByteArray(type))
                return "global::Wacs.ComponentModel.Harness" +
                    ".MemoryHelpers.LiftBytes(_memory!, "
                    + ptrLocal + ", " + lenLocal + ")";
            // Primitive arm in a mixed-shape result: read
            // slot 1 (`ptrLocal`) and cast back to the
            // primitive's C# type. Mirrors the packing in
            // `EmitPtrLenAssignInto`'s primitive branch.
            if (IsPrimitive(type))
            {
                switch (type)
                {
                    case "bool":
                    case "System.Boolean":
                        return "(" + ptrLocal + " != 0)";
                    case "float":
                    case "System.Single":
                        return "global::System.BitConverter" +
                            ".Int32BitsToSingle(" + ptrLocal
                            + ")";
                    case "int":
                    case "System.Int32":
                        return ptrLocal;
                    case "uint":
                    case "System.UInt32":
                        return "(uint)" + ptrLocal;
                    case "byte":
                    case "System.Byte":
                        return "(byte)" + ptrLocal;
                    case "sbyte":
                    case "System.SByte":
                        return "(sbyte)" + ptrLocal;
                    case "short":
                    case "System.Int16":
                        return "(short)" + ptrLocal;
                    case "ushort":
                    case "System.UInt16":
                        return "(ushort)" + ptrLocal;
                    default:
                        return "(" + type + ")"
                            + ptrLocal;
                }
            }
            if (IsPrimitiveArray(type))
            {
                string pElem = ArrayElementType(type);
                int pSize = PrimitiveSize(pElem);
                return "global::Wacs.ComponentModel.Harness" +
                    ".MemoryHelpers.LiftPrimitiveArray<"
                    + pElem + ">(_memory!, "
                    + ptrLocal + ", " + lenLocal + ", "
                    + pSize + ")";
            }
            // Simple list — string[] or T[][] for primitive
            // T. Routes through the dedicated MemoryHelpers
            // list helpers so the lift stays a single
            // expression (used by option / result arm
            // contexts and per-record-field list lifts).
            if (IsListType(type))
            {
                string elem = ArrayElementType(type);
                if (IsString(elem))
                    return "global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers" +
                        ".LiftStringList(_memory!, "
                        + ptrLocal + ", " + lenLocal + ")";
                if (IsPrimitiveArray(elem))
                {
                    string innerElem = ArrayElementType(elem);
                    int innerSize = PrimitiveSize(innerElem);
                    return "global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers" +
                        ".LiftPrimitiveArrayList<"
                        + innerElem + ">(_memory!, "
                        + ptrLocal + ", " + lenLocal + ", "
                        + innerSize + ")";
                }
            }
            return "default(" + type + ")";
        }

        // Emit shared option-lower statements: disc + payload
        // locals. Handles both flavors:
        //   * primitive inner (T?) — Nullable<T> with
        //     .HasValue / .GetValueOrDefault().
        //   * ptr/len inner (string? / byte[]?) — nullable
        //     ref type with == null check + conditional
        //     lower of the value into ptr/len locals.
        private static void EmitOptionArgLower(
            StringBuilder sb, string fieldType,
            string baseName, string sourceExpr)
        {
            string innerT = InnerOfNullable(fieldType);
            if (IsPtrLenAggregate(innerT))
            {
                sb.Append("            int ");
                sb.Append(baseName);
                sb.Append("_disc = ");
                sb.Append(sourceExpr);
                sb.AppendLine(" == null ? 0 : 1;");
                sb.Append("            int ");
                sb.Append(baseName);
                sb.AppendLine("_ptr = 0;");
                sb.Append("            int ");
                sb.Append(baseName);
                sb.AppendLine("_len = 0;");
                sb.Append("            if (");
                sb.Append(sourceExpr);
                sb.AppendLine(" != null)");
                sb.AppendLine("            {");
                EmitPtrLenAssignInto(sb, innerT,
                    sourceExpr,
                    baseName + "_ptr",
                    baseName + "_len",
                    "                ");
                sb.AppendLine("            }");
            }
            else
            {
                sb.Append("            int ");
                sb.Append(baseName);
                sb.Append("_disc = ");
                sb.Append(sourceExpr);
                sb.AppendLine(".HasValue ? 1 : 0;");
                sb.Append("            ");
                sb.Append(innerT);
                sb.Append(' ');
                sb.Append(baseName);
                sb.Append("_payload = ");
                sb.Append(sourceExpr);
                sb.AppendLine(".GetValueOrDefault();");
            }
        }

        // Emit shared result-lower statements: disc + payload
        // locals. Handles all four payload shapes:
        //   * joined == null (both arms unit) → disc only
        //   * joined primitive  → disc + payload
        //   * joined ptr/len arms (string / byte[]) → disc +
        //     (ptr, len) declared 0, assigned per arm via
        //     EmitPtrLenAssignInto. Unit arms leave (0, 0).
        // Used by both top-level result params and result
        // fields nested inside tuples / records.
        private static void EmitResultArgLower(
            StringBuilder sb, string fieldType,
            string baseName, string sourceExpr)
        {
            var (ok, err) = WitResultArgs(fieldType);
            var joined = ResultJoinedPayload(ok, err);
            sb.Append("            int ");
            sb.Append(baseName);
            sb.Append("_disc = ");
            sb.Append(sourceExpr);
            sb.AppendLine(".IsOk ? 0 : 1;");
            if (joined == null) return;
            if (IsPtrLenAggregate(joined))
            {
                sb.Append("            int ");
                sb.Append(baseName);
                sb.AppendLine("_ptr = 0;");
                sb.Append("            int ");
                sb.Append(baseName);
                sb.AppendLine("_len = 0;");
                sb.Append("            if (");
                sb.Append(sourceExpr);
                sb.AppendLine(".IsOk)");
                sb.AppendLine("            {");
                if (ok != ValueTupleType)
                    EmitPtrLenAssignInto(sb, ok,
                        sourceExpr + ".OkValue",
                        baseName + "_ptr",
                        baseName + "_len",
                        "                ");
                sb.AppendLine("            }");
                sb.AppendLine("            else");
                sb.AppendLine("            {");
                if (err != ValueTupleType)
                    EmitPtrLenAssignInto(sb, err,
                        sourceExpr + ".ErrValue",
                        baseName + "_ptr",
                        baseName + "_len",
                        "                ");
                sb.AppendLine("            }");
            }
            else
            {
                sb.Append("            ");
                sb.Append(joined);
                sb.Append(' ');
                sb.Append(baseName);
                sb.Append("_payload = ");
                sb.Append(sourceExpr);
                sb.Append(".IsOk ? ");
                sb.Append(ok == ValueTupleType
                    ? "default(" + joined + ")"
                    : sourceExpr + ".OkValue");
                sb.Append(" : ");
                sb.Append(err == ValueTupleType
                    ? "default(" + joined + ")"
                    : sourceExpr + ".ErrValue");
                sb.AppendLine(";");
            }
        }

        // Emit the flat-arg slots for one tuple-element /
        // record-field, given the locals bound by
        // EmitAggregateFieldLower. ptr/len aggregates expand
        // to <base>_ptr, <base>_len; option<T> expands to
        // <base>_disc, <base>_payload; result<TOk, TErr>
        // expands to <base>_disc (and <base>_payload when
        // joined != null); primitives are a single <base>.
        private static void EmitFlatArgsForField(
            StringBuilder sb, string fieldType, string baseName)
        {
            if (IsPtrLenAggregate(fieldType)
                || IsListType(fieldType))
            {
                sb.Append(baseName);
                sb.Append("_ptr, ");
                sb.Append(baseName);
                sb.Append("_len");
            }
            else if (IsOption(fieldType))
            {
                string innerT = InnerOfNullable(fieldType);
                if (IsPtrLenAggregate(innerT))
                {
                    sb.Append(baseName);
                    sb.Append("_disc, ");
                    sb.Append(baseName);
                    sb.Append("_ptr, ");
                    sb.Append(baseName);
                    sb.Append("_len");
                }
                else
                {
                    sb.Append(baseName);
                    sb.Append("_disc, ");
                    sb.Append(baseName);
                    sb.Append("_payload");
                }
            }
            else if (IsWitResult(fieldType))
            {
                var (ok, err) = WitResultArgs(fieldType);
                var joined = ResultJoinedPayload(ok, err);
                sb.Append(baseName);
                sb.Append("_disc");
                if (joined != null)
                {
                    sb.Append(", ");
                    if (IsPtrLenAggregate(joined))
                    {
                        sb.Append(baseName);
                        sb.Append("_ptr, ");
                        sb.Append(baseName);
                        sb.Append("_len");
                    }
                    else
                    {
                        sb.Append(baseName);
                        sb.Append("_payload");
                    }
                }
            }
            else if (TryLookupRecord(fieldType, out var subRec))
            {
                bool first = true;
                foreach (var f in subRec.Fields)
                {
                    if (!first) sb.Append(", ");
                    EmitFlatArgsForField(sb, f.Type,
                        baseName + "_" + f.Name);
                    first = false;
                }
            }
            else
            {
                sb.Append(baseName);
            }
        }

        private static bool AnyPtrLenAggregate(ExportMethod ex)
        {
            if (ex.ReturnType != null
                && (IsPtrLenAggregate(ex.ReturnType)
                    || IsListType(ex.ReturnType)))
                return true;
            foreach (var p in ex.Parameters)
                if (IsPtrLenAggregate(p.Type)
                    || IsListType(p.Type)) return true;
            return false;
        }

        // True iff any tuple or record-typed param or return
        // contains a ptr/len aggregate field. Needed to mark
        // the method as memory + realloc-using even when no
        // top-level string/byte[] is present.
        private static bool AnyAggregateFieldInTupleOrRecord(
            ExportMethod ex)
        {
            if (ex.ReturnType != null
                && AggregateContainsPtrLen(ex.ReturnType))
                return true;
            foreach (var p in ex.Parameters)
                if (AggregateContainsPtrLen(p.Type))
                    return true;
            return false;
        }

        private static bool AggregateContainsPtrLen(string fqType)
        {
            if (IsTuple(fqType))
            {
                foreach (var e in TupleElementTypes(fqType))
                    if (FieldContainsPtrLen(e)) return true;
            }
            if (TryLookupRecord(fqType, out var rec))
            {
                foreach (var f in rec.Fields)
                    if (FieldContainsPtrLen(f.Type)) return true;
            }
            return false;
        }

        // A field (or tuple element) contains a ptr/len when
        // it IS one, recursively when nested records / tuples
        // contain one, when a result arm is ptr/len, when
        // an option's inner is ptr/len, or when the field is
        // a list (the outer ptr is heap-allocated and the
        // inner element bodies — for list<string>,
        // list<byte[]> — are too).
        private static bool FieldContainsPtrLen(string fqType)
        {
            if (IsPtrLenAggregate(fqType)) return true;
            if (IsListType(fqType)) return true;
            if (IsOption(fqType))
                return IsPtrLenAggregate(
                    InnerOfNullable(fqType));
            if (IsWitResult(fqType))
                return ResultHasPtrLenArm(fqType);
            if (TryLookupRecord(fqType, out _)
                || IsTuple(fqType))
                return AggregateContainsPtrLen(fqType);
            return false;
        }

        // Flat (canon-ABI lowered) generic args for
        // CreateInvokerFunc<...> / CreateInvokerAction<...>.
        // Each ptr/len aggregate expands to two ints; each
        // option<T> expands to (disc:i32, T-slot); a
        // multi-slot return (string, byte[], option<T>) becomes
        // a single i32 retArea.
        private static string BuildFlatInvokerTypeArgs(
            ExportMethod ex)
        {
            int paramSlots = CountFlatSlots(ex);
            bool hasReturn = ex.ReturnType != null;
            if (paramSlots == 0 && !hasReturn) return "";

            var sb = new StringBuilder();
            sb.Append('<');
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (IsPtrLenAggregate(p.Type)
                    || IsListType(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int, int");
                    first = false;
                }
                else if (IsOption(p.Type))
                {
                    if (!first) sb.Append(", ");
                    string innerT = InnerOfNullable(p.Type);
                    if (IsPtrLenAggregate(innerT))
                        sb.Append("int, int, int");
                    else
                    {
                        sb.Append("int, ");
                        sb.Append(innerT);
                    }
                    first = false;
                }
                else if (IsWitResult(p.Type))
                {
                    var (ok, err) = WitResultArgs(p.Type);
                    var joined = ResultJoinedPayload(ok, err);
                    if (!first) sb.Append(", ");
                    sb.Append("int");
                    if (joined != null)
                    {
                        sb.Append(", ");
                        if (IsPtrLenAggregate(joined))
                            sb.Append("int, int");
                        else
                            sb.Append(joined);
                    }
                    first = false;
                }
                else if (IsTuple(p.Type))
                {
                    var elems = TupleElementTypes(p.Type);
                    for (int i = 0; i < elems.Count; i++)
                    {
                        if (!first) sb.Append(", ");
                        AppendFlatFieldTypes(sb, elems[i]);
                        first = false;
                    }
                }
                else if (TryLookupRecord(p.Type, out var rec))
                {
                    foreach (var f in rec.Fields)
                    {
                        if (!first) sb.Append(", ");
                        AppendFlatFieldTypes(sb, f.Type);
                        first = false;
                    }
                }
                else
                {
                    if (!first) sb.Append(", ");
                    sb.Append(p.Type);
                    first = false;
                }
            }
            if (hasReturn)
            {
                if (!first) sb.Append(", ");
                if (UsesRetArea(ex.ReturnType!))
                    sb.Append("int");
                else if (IsWitResult(ex.ReturnType!))
                {
                    // result<(), ()> flat is just the disc
                    // i32 (returned directly when both arms
                    // are unit). The retArea path already
                    // handled above for non-trivial arms.
                    sb.Append("int");
                }
                else if (IsTuple(ex.ReturnType!))
                {
                    // Reach here only when UsesRetArea==false
                    // → total slots == 1, so the sole element
                    // is either a primitive (slot = its type)
                    // or a result<(), ()> (slot = int).
                    var elems = TupleElementTypes(ex.ReturnType!);
                    sb.Append(IsWitResult(elems[0])
                        ? "int" : elems[0]);
                }
                else if (TryLookupRecord(ex.ReturnType!,
                    out var recRet))
                {
                    sb.Append(IsWitResult(recRet.Fields[0].Type)
                        ? "int" : recRet.Fields[0].Type);
                }
                else
                    sb.Append(ex.ReturnType);
            }
            sb.Append('>');
            return sb.ToString();
        }

        private static int CountFlatSlots(ExportMethod ex)
        {
            int slots = 0;
            foreach (var p in ex.Parameters)
            {
                if (IsPtrLenAggregate(p.Type)) slots += 2;
                else if (IsListType(p.Type)) slots += 2;
                else if (IsOption(p.Type))
                {
                    string innerT = InnerOfNullable(p.Type);
                    slots += 1 + (IsPtrLenAggregate(innerT)
                        ? 2 : 1);
                }
                else if (IsWitResult(p.Type))
                {
                    var (ok, err) = WitResultArgs(p.Type);
                    var joined = ResultJoinedPayload(ok, err);
                    if (joined == null) slots += 1;
                    else slots += 1
                        + (IsPtrLenAggregate(joined) ? 2 : 1);
                }
                else if (IsTuple(p.Type))
                {
                    // tuple<T1, T2, ...> param flat-lowers to
                    // its fields. Primitives are 1 slot;
                    // ptr/len aggregates (string, byte[]) are
                    // 2 slots.
                    foreach (var e in TupleElementTypes(p.Type))
                        slots += FieldSlotCount(e);
                }
                else if (TryLookupRecord(p.Type,
                    out var recLayout))
                {
                    // record param flat-lowers to its fields
                    // in declaration order. Same slot count
                    // rule as tuples.
                    foreach (var f in recLayout.Fields)
                        slots += FieldSlotCount(f.Type);
                }
                else slots += 1;
            }
            return slots;
        }

        // Multi-slot return types (string, byte[], option<T>,
        // result<TOk,TErr> with a payload arm) flatten to a
        // single i32 retArea pointer — the callee writes the
        // tuple into linear memory and returns the address.
        // result<(), ()> is the exception: flat is just (disc)
        // which is returned directly as an i32.
        private static bool UsesRetArea(string fqType)
        {
            if (IsPtrLenAggregate(fqType)) return true;
            if (IsListType(fqType)) return true;
            if (IsOption(fqType)) return true;
            if (IsWitResult(fqType))
            {
                var (ok, err) = WitResultArgs(fqType);
                return ResultJoinedPayload(ok, err) != null;
            }
            // Tuple / record: total flat slots > 1 → retArea.
            // Per-element slot count covers option (2) and
            // result-with-joined (2), so a single-element
            // tuple/record holding one of those still routes
            // through retArea.
            if (IsTuple(fqType))
                return TotalFlatSlotsForTuple(fqType) > 1;
            if (TryLookupRecord(fqType, out var rec))
                return TotalFlatSlotsForRecord(rec) > 1;
            return false;
        }

        private static int TotalFlatSlotsForTuple(string fqType)
        {
            int sum = 0;
            foreach (var e in TupleElementTypes(fqType))
                sum += FieldSlotCount(e);
            return sum;
        }

        private static int TotalFlatSlotsForRecord(
            RecordLayout rec)
        {
            int sum = 0;
            foreach (var f in rec.Fields)
                sum += FieldSlotCount(f.Type);
            return sum;
        }


        // Field type matching the FLAT (canon-ABI lowered)
        // signature — string → int, int; string return →
        // int retArea. Used for the memoized invoker field
        // declaration.
        private static string BuildInvokerDelegateType(
            ExportMethod ex)
        {
            int paramSlots = CountFlatSlots(ex);
            bool hasReturn = ex.ReturnType != null;

            var sb = new StringBuilder();
            if (!hasReturn)
            {
                if (paramSlots == 0)
                    return "System.Action?";
                sb.Append("System.Action<");
                AppendFlatParamTypes(sb, ex);
                sb.Append(">?");
                return sb.ToString();
            }
            sb.Append("System.Func<");
            if (paramSlots > 0)
            {
                AppendFlatParamTypes(sb, ex);
                sb.Append(", ");
            }
            if (UsesRetArea(ex.ReturnType!))
                sb.Append("int");
            else if (IsWitResult(ex.ReturnType!))
                sb.Append("int"); // unit/unit result → disc
            else if (IsTuple(ex.ReturnType!))
            {
                // UsesRetArea==false here means total slots
                // == 1: single-element tuple whose element
                // is a 1-slot field (primitive or
                // result<(), ()>). Emit the underlying slot
                // type — `int` for the result-disc form.
                var elems = TupleElementTypes(ex.ReturnType!);
                sb.Append(IsWitResult(elems[0])
                    ? "int" : elems[0]);
            }
            else if (TryLookupRecord(ex.ReturnType!,
                out var recT))
            {
                // Same single-slot reasoning as the tuple
                // branch — the sole field is either a
                // primitive or a result<(), ()>.
                sb.Append(IsWitResult(recT.Fields[0].Type)
                    ? "int" : recT.Fields[0].Type);
            }
            else
                sb.Append(ex.ReturnType);
            sb.Append(">?");
            return sb.ToString();
        }

        private static void AppendFlatParamTypes(
            StringBuilder sb, ExportMethod ex)
        {
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (IsPtrLenAggregate(p.Type)
                    || IsListType(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int, int");
                    first = false;
                }
                else if (IsOption(p.Type))
                {
                    // option<T> flat lowering: (i32 disc,
                    // payload-slots). primitive T = 1 slot,
                    // ptr/len T (string/byte[]) = 2 slots.
                    if (!first) sb.Append(", ");
                    string innerT = InnerOfNullable(p.Type);
                    if (IsPtrLenAggregate(innerT))
                        sb.Append("int, int, int");
                    else
                    {
                        sb.Append("int, ");
                        sb.Append(innerT);
                    }
                    first = false;
                }
                else if (IsWitResult(p.Type))
                {
                    // result<TOk, TErr> flat lowering:
                    // (i32 disc, joined-payload). joined-
                    // payload is null when both arms are
                    // unit (no payload slot).
                    var (ok, err) = WitResultArgs(p.Type);
                    var joined = ResultJoinedPayload(ok, err);
                    if (!first) sb.Append(", ");
                    sb.Append("int");
                    if (joined != null)
                    {
                        sb.Append(", ");
                        sb.Append(joined);
                    }
                    first = false;
                }
                else if (IsTuple(p.Type))
                {
                    // tuple<T1, T2, ...> param flattens to
                    // its fields. Per-field expansion lives
                    // in AppendFlatFieldTypes.
                    var elems = TupleElementTypes(p.Type);
                    for (int i = 0; i < elems.Count; i++)
                    {
                        if (!first) sb.Append(", ");
                        AppendFlatFieldTypes(sb, elems[i]);
                        first = false;
                    }
                }
                else if (TryLookupRecord(p.Type, out var rec))
                {
                    foreach (var f in rec.Fields)
                    {
                        if (!first) sb.Append(", ");
                        AppendFlatFieldTypes(sb, f.Type);
                        first = false;
                    }
                }
                else
                {
                    if (!first) sb.Append(", ");
                    sb.Append(p.Type);
                    first = false;
                }
            }
        }

        // Per-field flat-type expansion. ptr/len aggregates →
        // (int, int); option<T> → (int, T); result<TOk, TErr>
        // → (int) or (int, joined); primitives → themselves.
        // Shared by tuple + record param expansion in
        // AppendFlatParamTypes.
        private static void AppendFlatFieldTypes(
            StringBuilder sb, string fieldType)
        {
            if (IsPtrLenAggregate(fieldType)
                || IsListType(fieldType))
            {
                sb.Append("int, int");
            }
            else if (IsOption(fieldType))
            {
                string innerT = InnerOfNullable(fieldType);
                if (IsPtrLenAggregate(innerT))
                    sb.Append("int, int, int");
                else
                {
                    sb.Append("int, ");
                    sb.Append(innerT);
                }
            }
            else if (IsWitResult(fieldType))
            {
                var (ok, err) = WitResultArgs(fieldType);
                var joined = ResultJoinedPayload(ok, err);
                sb.Append("int");
                if (joined != null)
                {
                    sb.Append(", ");
                    if (IsPtrLenAggregate(joined))
                        sb.Append("int, int");
                    else
                        sb.Append(joined);
                }
            }
            else if (TryLookupRecord(fieldType, out var subRec))
            {
                bool first = true;
                foreach (var f in subRec.Fields)
                {
                    if (!first) sb.Append(", ");
                    AppendFlatFieldTypes(sb, f.Type);
                    first = false;
                }
            }
            else
            {
                sb.Append(fieldType);
            }
        }

        // Build the <T1,...> chunk for the CreateInvokerFunc /
        // CreateInvokerAction generic args.
        private static string BuildInvokerTypeArgs(
            ExportMethod ex)
        {
            if (ex.Parameters.Length == 0 && ex.ReturnType == null)
                return "";
            var sb = new StringBuilder();
            sb.Append('<');
            if (ex.Parameters.Length > 0)
            {
                AppendParamTypes(sb, ex);
                if (ex.ReturnType != null) sb.Append(", ");
            }
            if (ex.ReturnType != null) sb.Append(ex.ReturnType);
            sb.Append('>');
            return sb.ToString();
        }

        private static void AppendParamTypes(
            StringBuilder sb, ExportMethod ex)
        {
            for (int i = 0; i < ex.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ex.Parameters[i].Type);
            }
        }

        private static string EscapeStringLiteral(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
