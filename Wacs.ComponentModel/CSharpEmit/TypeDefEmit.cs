// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Text;
using Wacs.ComponentModel.Types;

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// Emits wit-bindgen-csharp-shaped nested type declarations
    /// inside an interface file. Each WIT named type (<c>record</c>,
    /// <c>variant</c>, <c>enum</c>, <c>flags</c>, <c>resource</c>,
    /// plus type aliases) becomes a C# class/struct/enum nested
    /// inside the <c>public interface I{Name} { … }</c>.
    ///
    /// <para><b>Phase 1a.2 scope:</b> <c>enum</c> and <c>flags</c>.
    /// <c>record</c> / <c>variant</c> / <c>resource</c> / type
    /// aliases are follow-up commits — each has its own shape and
    /// deserves its own emitter + snapshot test.</para>
    /// </summary>
    internal static class TypeDefEmit
    {
        /// <summary>
        /// Emit a <see cref="CtNamedType"/> as a nested type inside
        /// the interface (4-space indented from the interface body).
        /// Returns the source text with a trailing newline after the
        /// closing brace. Callers control inter-type spacing.
        /// </summary>
        public static string Emit(CtNamedType named)
        {
            return named.Type switch
            {
                CtEnumType e => EmitEnum(named.Name, e),
                CtFlagsType f => EmitFlags(named.Name, f),
                CtRecordType r => EmitRecord(named.Name, r),
                CtVariantType v => EmitVariant(named.Name, v),
                _ => throw new NotImplementedException(
                    "Type emission for " + named.Type.GetType().Name +
                    " is a Phase 1a.2 follow-up."),
            };
        }

        /// <summary>
        /// Overload that takes the owning interface so resource
        /// emission can derive the DllImport entry-point base. Use
        /// this from the interface-file emitter; the ambient
        /// <see cref="Emit(CtNamedType)"/> handles everything else.
        /// </summary>
        public static string Emit(CtNamedType named, CtInterfaceType owner)
        {
            if (named.Type is CtResourceType r)
                return EmitResource(named.Name, r, owner);
            return Emit(named);
        }

        // ---- enum ----------------------------------------------------------

        /// <summary>
        /// Plain C# enum, no backing type (defaults to int).
        /// wit-bindgen-csharp places all cases on a single line,
        /// comma-separated, inside braces on separate lines:
        /// <code>
        ///     public enum Color {
        ///         RED, GREEN, BLUE
        ///     }
        /// </code>
        /// Case names are UPPER_SNAKE_CASE of the WIT names.
        /// </summary>
        private static string EmitEnum(string name, CtEnumType e)
        {
            var sb = new StringBuilder();
            sb.Append("    public enum ");
            sb.Append(NameConventions.ToPascalCase(name));
            sb.Append(" {\n");
            sb.Append("        ");
            for (int i = 0; i < e.Cases.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(NameConventions.ToUpperSnake(e.Cases[i]));
            }
            sb.Append('\n');
            sb.Append("    }\n");
            return sb.ToString();
        }

        // ---- flags ---------------------------------------------------------

        /// <summary>
        /// Bit-flag enum with explicit <c>1 &lt;&lt; i</c> values and
        /// a backing type sized to the flag count: byte (≤8),
        /// ushort (≤16), uint (≤32), ulong (≤64). Note: NO
        /// <c>[Flags]</c> attribute — wit-bindgen-csharp 0.30.0
        /// doesn't emit one.
        /// </summary>
        private static string EmitFlags(string name, CtFlagsType f)
        {
            var sb = new StringBuilder();
            sb.Append("    public enum ");
            sb.Append(NameConventions.ToPascalCase(name));
            sb.Append(" : ");
            sb.Append(FlagsBackingType(f.Flags.Count));
            sb.Append(" {\n");
            for (int i = 0; i < f.Flags.Count; i++)
            {
                sb.Append("        ");
                sb.Append(NameConventions.ToUpperSnake(f.Flags[i]));
                sb.Append(" = 1 << ");
                sb.Append(i);
                sb.Append(",\n");
            }
            sb.Append("    }\n");
            return sb.ToString();
        }

        // ---- record --------------------------------------------------------

        /// <summary>
        /// WIT <c>record</c> → plain C# class with
        /// <c>public readonly</c> fields + a positional constructor.
        /// Field names are kebab→camelCase; the type name is
        /// kebab→PascalCase. Verified against wit-bindgen-csharp
        /// 0.30.0 on two fixtures.
        /// <code>
        ///     public class WebAddress {
        ///         public readonly string hostName;
        ///         public readonly ushort portNumber;
        ///
        ///         public WebAddress(string hostName, ushort portNumber) {
        ///             this.hostName = hostName;
        ///             this.portNumber = portNumber;
        ///         }
        ///     }
        /// </code>
        /// </summary>
        private static string EmitRecord(string name, CtRecordType r)
        {
            var className = NameConventions.ToPascalCase(name);
            var sb = new StringBuilder();
            sb.Append("    public class ");
            sb.Append(className).Append(" {\n");

            foreach (var f in r.Fields)
            {
                sb.Append("        public readonly ");
                sb.Append(TypeRefEmit.EmitParam(f.Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(f.Name));
                sb.Append(";\n");
            }

            sb.Append("\n");
            sb.Append("        public ").Append(className).Append('(');
            for (int i = 0; i < r.Fields.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TypeRefEmit.EmitParam(r.Fields[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(r.Fields[i].Name));
            }
            sb.Append(") {\n");
            foreach (var f in r.Fields)
            {
                var fld = NameConventions.ToCamelCase(f.Name);
                sb.Append("            this.").Append(fld);
                sb.Append(" = ").Append(fld).Append(";\n");
            }
            sb.Append("        }\n");
            sb.Append("    }\n");
            return sb.ToString();
        }

        // ---- variant -------------------------------------------------------

        /// <summary>
        /// WIT <c>variant</c> → C# class implementing a
        /// discriminated union via a <c>byte Tag</c> field plus an
        /// <c>object? value</c> payload. One static factory per
        /// case (with or without payload), an <c>As{Case}</c>
        /// property per payload-carrying case, and
        /// <c>public const byte {CASE_UPPER} = i;</c> discriminant
        /// constants at the bottom.
        ///
        /// <para>Verified against wit-bindgen-csharp 0.30.0 on a
        /// fixture with primitive, tuple, string, and no-payload
        /// cases. Cross-interface payload types (the
        /// <c>global::X.Y.IError.Error</c>-style qualified reference
        /// used for <c>StreamError.lastOperationFailed(error)</c>)
        /// are a separate cross-interface-qualifier follow-up — for
        /// now payloads are emitted through <c>TypeRefEmit.EmitParam</c>
        /// and so same-interface and primitive/aggregate payloads
        /// work; cross-interface refs produce an unqualified name
        /// that won't compile until the qualifier lands.</para>
        /// </summary>
        private static string EmitVariant(string name, CtVariantType v)
        {
            var className = NameConventions.ToPascalCase(name);
            var sb = new StringBuilder();
            sb.Append("    public class ").Append(className).Append(" {\n");
            sb.Append("        public readonly byte Tag;\n");
            sb.Append("        private readonly object? value;\n\n");

            sb.Append("        private ").Append(className)
              .Append("(byte tag, object? value) {\n");
            sb.Append("            this.Tag = tag;\n");
            sb.Append("            this.value = value;\n");
            sb.Append("        }\n\n");

            // Static factories — one per case (payload or no-payload).
            for (int i = 0; i < v.Cases.Count; i++)
            {
                var c = v.Cases[i];
                var caseCamel = NameConventions.ToCamelCase(c.Name);
                var caseUpper = NameConventions.ToUpperSnake(c.Name);

                sb.Append("        public static ").Append(className)
                  .Append(' ').Append(caseCamel).Append('(');
                if (c.Payload != null)
                {
                    sb.Append(TypeRefEmit.EmitParam(c.Payload));
                    sb.Append(' ').Append(caseCamel);
                }
                sb.Append(") {\n");
                sb.Append("            return new ").Append(className)
                  .Append('(').Append(caseUpper).Append(", ");
                sb.Append(c.Payload != null ? caseCamel : "null");
                sb.Append(");\n");
                sb.Append("        }\n\n");
            }

            // As{Case} properties — only payload-carrying cases.
            foreach (var c in v.Cases)
            {
                if (c.Payload == null) continue;
                var casePascal = NameConventions.ToPascalCase(c.Name);
                var caseUpper = NameConventions.ToUpperSnake(c.Name);
                var payloadType = TypeRefEmit.EmitParam(c.Payload);

                sb.Append("        public ").Append(payloadType)
                  .Append(" As").Append(casePascal).Append('\n');
                sb.Append("        {\n");
                sb.Append("            get\n");
                sb.Append("            {\n");
                sb.Append("                if (Tag == ").Append(caseUpper).Append(")\n");
                sb.Append("                return (").Append(payloadType).Append(")value!;\n");
                sb.Append("                else\n");
                sb.Append("                throw new ArgumentException(\"expected ")
                  .Append(caseUpper).Append(", got \" + Tag);\n");
                sb.Append("            }\n");
                sb.Append("        }\n\n");
            }

            // Discriminant constants — no blank lines between.
            for (int i = 0; i < v.Cases.Count; i++)
            {
                var caseUpper = NameConventions.ToUpperSnake(v.Cases[i].Name);
                sb.Append("        public const byte ")
                  .Append(caseUpper).Append(" = ").Append(i).Append(";\n");
            }

            sb.Append("    }\n");
            return sb.ToString();
        }

        // ---- resource -----------------------------------------------------

        /// <summary>
        /// WIT <c>resource</c> → C# class : IDisposable with a
        /// <c>Handle</c> property, <c>THandle</c> record struct,
        /// resource-drop DllImport, Dispose/Finalizer pattern, and
        /// one internal stub class + public wrapper method per
        /// resource method.
        ///
        /// <para>Matches wit-bindgen-csharp 0.30.0's output for
        /// instance methods with primitive params/return. Static
        /// and constructor methods, plus aggregate-typed signatures,
        /// are follow-ups.</para>
        /// </summary>
        private static string EmitResource(string name, CtResourceType r,
                                           CtInterfaceType owner)
        {
            var className = NameConventions.ToPascalCase(name);
            var entryPointBase = BuildEntryPointBase(owner);
            var sb = new StringBuilder();

            sb.Append("    public class ").Append(className).Append(": IDisposable {\n");
            sb.Append("        internal int Handle { get; set; }\n\n");

            sb.Append("        public readonly record struct THandle(int Handle);\n\n");

            sb.Append("        public ").Append(className).Append("(THandle handle) {\n");
            sb.Append("            Handle = handle.Handle;\n");
            sb.Append("        }\n\n");

            sb.Append("        public void Dispose() {\n");
            sb.Append("            Dispose(true);\n");
            sb.Append("            GC.SuppressFinalize(this);\n");
            sb.Append("        }\n\n");

            sb.Append("        [DllImport(\"").Append(entryPointBase);
            sb.Append("\", EntryPoint = \"[resource-drop]").Append(name);
            sb.Append("\"), WasmImportLinkage]\n");
            sb.Append("        private static extern void wasmImportResourceDrop(int p0);\n\n");

            sb.Append("        protected virtual void Dispose(bool disposing) {\n");
            sb.Append("            if (Handle != 0) {\n");
            sb.Append("                wasmImportResourceDrop(Handle);\n");
            sb.Append("                Handle = 0;\n");
            sb.Append("            }\n");
            sb.Append("        }\n\n");

            sb.Append("        ~").Append(className).Append("() {\n");
            sb.Append("            Dispose(false);\n");
            sb.Append("        }\n\n");

            // Instance methods.
            foreach (var m in r.Methods)
            {
                if (m.Kind != CtResourceMethodKind.Instance)
                    throw new System.NotImplementedException(
                        "Resource " + m.Kind.ToString().ToLowerInvariant() +
                        " methods are a Phase 1a.2 follow-up.");

                EmitResourceMethod(sb, m, name, entryPointBase);
            }

            sb.Append("    }\n");
            return sb.ToString();
        }

        private static void EmitResourceMethod(StringBuilder sb,
                                               CtResourceMethod m,
                                               string resourceWitName,
                                               string entryPointBase)
        {
            var methodName = NameConventions.ToPascalCase(m.Name!);
            var stubClass = methodName + "WasmInterop";
            var sig = m.Function;

            // Stub class
            sb.Append("        internal static class ").Append(stubClass).Append('\n');
            sb.Append("        {\n");
            sb.Append("            [DllImport(\"").Append(entryPointBase);
            sb.Append("\", EntryPoint = \"[method]").Append(resourceWitName);
            sb.Append('.').Append(m.Name).Append("\"), WasmImportLinkage]\n");

            sb.Append("            internal static extern ");
            sb.Append(StubReturnType(sig));
            sb.Append(" wasmImport").Append(methodName).Append("(int p0");
            for (int i = 0; i < sig.Params.Count; i++)
            {
                sb.Append(", ");
                sb.Append(PrimStubType(sig.Params[i].Type));
                sb.Append(" p").Append(i + 1);
            }
            sb.Append(");\n");
            sb.Append("\n        }\n\n");

            // Wrapper method. `public   unsafe` has the double
            // space verbatim (wit-bindgen quirk preserved).
            sb.Append("        public   unsafe ");
            sb.Append(FunctionEmit.EmitReturnType(sig));
            sb.Append(' ').Append(methodName).Append('(');
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TypeRefEmit.EmitParam(sig.Params[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(sig.Params[i].Name));
            }
            sb.Append(")\n");
            sb.Append("        {\n");
            sb.Append("            var handle = this.Handle;\n");

            var retType = FunctionEmit.EmitReturnType(sig);
            var isVoid = retType == "void";

            // Stub call — for resource methods, wit-bindgen uses
            // `var result =  Stub.wasmImportX(handle, args)` with
            // the same double-space quirk for non-void, and a plain
            // `Stub.wasmImportX(handle, args);` for void.
            sb.Append("            ");
            if (!isVoid) sb.Append("var result =  ");
            sb.Append(stubClass).Append(".wasmImport").Append(methodName);
            sb.Append("(handle");
            for (int i = 0; i < sig.Params.Count; i++)
            {
                sb.Append(", ");
                var argName = NameConventions.ToCamelCase(sig.Params[i].Name);
                if (sig.Params[i].Type is CtPrimType p)
                    sb.Append(PrimMarshal.Lower(p.Kind, argName));
                else
                    sb.Append(argName);
            }
            sb.Append(");\n");

            if (!isVoid)
            {
                sb.Append("            return ");
                if (sig.Result is CtPrimType rp)
                    sb.Append(PrimMarshal.Lift(rp.Kind, "result"));
                else
                    sb.Append("result");
                sb.Append(";\n");
            }

            sb.Append("\n");
            sb.Append("            //TODO: free alloc handle (interopString) if exists\n");
            sb.Append("        }\n\n");
        }

        private static string PrimStubType(CtValType t)
        {
            if (t is CtPrimType p) return PrimMarshal.StubType(p.Kind);
            throw new System.NotImplementedException(
                "Resource stub type for " + t.GetType().Name +
                " is a follow-up.");
        }

        private static string StubReturnType(CtFunctionType sig)
        {
            if (sig.HasNoResult) return "void";
            if (sig.Result == null) return "void";
            return PrimStubType(sig.Result);
        }

        private static string BuildEntryPointBase(CtInterfaceType iface)
        {
            // `{ns}:{path}/{iface-name}[@ver]` — same pattern as
            // InteropEmit's private helper. Duplicate rather than
            // thread-internal-shared through the API for now.
            var sb = new StringBuilder();
            sb.Append(iface.Package!.Namespace);
            foreach (var seg in iface.Package.Path)
            {
                sb.Append(':');
                sb.Append(seg);
            }
            sb.Append('/').Append(iface.Name);
            if (iface.Package.Version != null)
                sb.Append('@').Append(iface.Package.Version);
            return sb.ToString();
        }

        private static string FlagsBackingType(int flagCount)
        {
            if (flagCount <= 8) return "byte";
            if (flagCount <= 16) return "ushort";
            if (flagCount <= 32) return "uint";
            if (flagCount <= 64) return "ulong";
            throw new ArgumentOutOfRangeException(
                nameof(flagCount),
                "Flags type with more than 64 members is not supported " +
                "(component-model spec limit).");
        }
    }
}
