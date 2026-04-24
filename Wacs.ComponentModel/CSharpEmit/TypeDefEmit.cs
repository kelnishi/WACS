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
            var body = named.Type switch
            {
                CtEnumType e => EmitEnum(named.Name, e),
                CtFlagsType f => EmitFlags(named.Name, f),
                CtRecordType r => EmitRecord(named.Name, r),
                CtVariantType v => EmitVariant(named.Name, v),
                _ => throw new NotImplementedException(
                    "Type emission for " + named.Type.GetType().Name +
                    " is a Phase 1a.2 follow-up."),
            };
            return PrependDocs(named, MaybePrependWitName(named.Name, body));
        }

        /// <summary>
        /// If <see cref="EmitAmbient.IncludeWitMetadata"/> is on,
        /// prepend a <c>[global::Wacs.ComponentModel.WitName("kebab")]</c>
        /// attribute line to the emitted type declaration.
        /// Fully qualified path so consumers don't need a
        /// <c>using Wacs.ComponentModel;</c> directive in the
        /// generated file.
        /// </summary>
        private static string MaybePrependWitName(string kebabName, string body)
        {
            if (!EmitAmbient.IncludeWitMetadata) return body;
            return "    [global::Wacs.ComponentModel.WitName(\""
                + kebabName + "\")]\n" + body;
        }

        /// <summary>
        /// Prepend a C# <c>/** … */</c> doc-comment block when
        /// <paramref name="named"/> carries
        /// <see cref="CtNamedType.DocLines"/>. Shape matches
        /// wit-bindgen 0.30.0's output: opening <c>/**</c>, one
        /// <c>* </c>-prefixed line per WIT doc line (no marker for
        /// blank lines — just <c>*</c>), closing <c>*/</c>, then a
        /// trailing blank line before the declaration. The body
        /// is expected to begin at 4-space indent (standard nested
        /// type indent). No-op when there are no doc lines.
        /// </summary>
        private static string PrependDocs(CtNamedType named, string body)
        {
            if (named.DocLines == null || named.DocLines.Count == 0)
                return body;
            var sb = new StringBuilder();
            sb.Append("    /**\n");
            foreach (var line in named.DocLines)
            {
                if (line.Length == 0) sb.Append("    *\n");
                else sb.Append("    * ").Append(EscapeXml(line)).Append('\n');
            }
            sb.Append("    */\n");
            sb.Append("\n");
            sb.Append(body);
            return sb.ToString();
        }

        /// <summary>
        /// Escape <c>&lt;</c> / <c>&gt;</c> / <c>&amp;</c> for
        /// safe inclusion in a C# <c>/** */</c> doc-comment block.
        /// Matches wit-bindgen 0.30.0 — real-world WIT doc comments
        /// frequently use generics-like syntax (e.g.
        /// <c>borrow&lt;error&gt;</c>) which would otherwise be
        /// parsed as XML tags by the C# doc processor.
        /// </summary>
        private static string EscapeXml(string s)
        {
            if (s.IndexOfAny(new[] { '<', '>', '&' }) < 0) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '&': sb.Append("&amp;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
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
                return PrependDocs(named,
                    MaybePrependWitName(named.Name,
                        EmitResource(named.Name, r, owner)));
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
                if (EmitAmbient.IncludeWitMetadata
                    && NameConventions.ToCamelCase(f.Name) != f.Name)
                {
                    // Emit WitName when the kebab WIT name differs
                    // from the camelCase C# field name. Same-case
                    // names (already lowercase-simple) don't need
                    // the attribute — keeps output tidy.
                    sb.Append("        [global::Wacs.ComponentModel.WitName(\"")
                      .Append(f.Name).Append("\")]\n");
                }
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
            var entryPointBase = EntryPoints.InterfaceBase(owner);
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
            sb.Append("\", EntryPoint = \"")
              .Append(EntryPoints.ResourceDrop(name))
              .Append("\"), WasmImportLinkage]\n");
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

            // Emit each resource method in its appropriate shape.
            foreach (var m in r.Methods)
            {
                switch (m.Kind)
                {
                    case CtResourceMethodKind.Constructor:
                        EmitResourceConstructor(sb, m, className, name, entryPointBase);
                        break;
                    case CtResourceMethodKind.Static:
                        EmitResourceStatic(sb, m, className, name, entryPointBase, owner);
                        break;
                    case CtResourceMethodKind.Instance:
                        EmitResourceMethod(sb, m, name, entryPointBase);
                        break;
                }
            }

            sb.Append("    }\n");
            return sb.ToString();
        }

        /// <summary>
        /// Resource constructor emission. WIT
        /// <c>constructor(args)</c> → a public C# constructor that
        /// calls a <c>[constructor]resource-name</c> DllImport stub
        /// and assigns the returned handle to <c>this.Handle</c>.
        /// Note the verbatim <c>public   unsafe  {Class}</c>
        /// whitespace oddity — wit-bindgen 0.30.0 preserves it.
        /// </summary>
        private static void EmitResourceConstructor(StringBuilder sb,
                                                    CtResourceMethod m,
                                                    string className,
                                                    string resourceWitName,
                                                    string entryPointBase)
        {
            var sig = m.Function;
            sb.Append("        internal static class ConstructorWasmInterop\n");
            sb.Append("        {\n");
            sb.Append("            [DllImport(\"").Append(entryPointBase);
            sb.Append("\", EntryPoint = \"")
              .Append(EntryPoints.ResourceConstructor(resourceWitName))
              .Append("\"), WasmImportLinkage]\n");
            sb.Append("            internal static extern int wasmImportConstructor(");
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PrimStubType(sig.Params[i].Type)).Append(" p").Append(i);
            }
            sb.Append(");\n\n        }\n\n");

            // The constructor — note the verbatim triple-space
            // formatting: "public   unsafe  {ClassName}".
            sb.Append("        public   unsafe  ").Append(className).Append('(');
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                FunctionEmit.EmitParamWitName(sb, sig.Params[i].Name);
                sb.Append(TypeRefEmit.EmitParam(sig.Params[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(sig.Params[i].Name));
            }
            sb.Append(")\n");
            sb.Append("        {\n");
            sb.Append("            var result =  ConstructorWasmInterop.wasmImportConstructor(");
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var argName = NameConventions.ToCamelCase(sig.Params[i].Name);
                if (sig.Params[i].Type is CtPrimType p)
                    sb.Append(PrimMarshal.Lower(p.Kind, argName));
                else
                    sb.Append(argName);
            }
            sb.Append(");\n");
            sb.Append("            this.Handle = result;\n\n");
            sb.Append("            //TODO: free alloc handle (interopString) if exists\n");
            sb.Append("        }\n\n");
        }

        /// <summary>
        /// Resource static method emission. WIT <c>static name:
        /// func(...)</c> → <c>public static</c> C# method that calls
        /// a <c>[static]resource-name.method-name</c> DllImport stub.
        /// When the static method returns the resource class itself,
        /// wit-bindgen qualifies the return type with the full
        /// <c>global::{WorldNs}.wit.imports.{pkg}.I{Iface}.{Class}</c>
        /// path — reproduced here for byte-for-byte parity.
        /// </summary>
        private static void EmitResourceStatic(StringBuilder sb,
                                               CtResourceMethod m,
                                               string className,
                                               string resourceWitName,
                                               string entryPointBase,
                                               CtInterfaceType owner)
        {
            var methodName = NameConventions.ToPascalCase(m.Name!);
            var stubClass = methodName + "WasmInterop";
            var sig = m.Function;

            sb.Append("        internal static class ").Append(stubClass).Append('\n');
            sb.Append("        {\n");
            sb.Append("            [DllImport(\"").Append(entryPointBase);
            sb.Append("\", EntryPoint = \"")
              .Append(EntryPoints.ResourceStatic(resourceWitName, m.Name!))
              .Append("\"), WasmImportLinkage]\n");
            sb.Append("            internal static extern ").Append(StubReturnType(sig));
            sb.Append(" wasmImport").Append(methodName).Append('(');
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(PrimStubType(sig.Params[i].Type)).Append(" p").Append(i);
            }
            sb.Append(");\n\n        }\n\n");

            // Static methods that return the resource itself carry
            // a fully qualified return type.
            var returnsSelf = ReturnsSelfResource(sig, resourceWitName);
            var qualifiedClass = QualifiedResourceTypeName(owner, className);

            sb.Append("        public  static unsafe ");
            sb.Append(returnsSelf ? qualifiedClass : FunctionEmit.EmitReturnType(sig));
            sb.Append(' ').Append(methodName).Append('(');
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                FunctionEmit.EmitParamWitName(sb, sig.Params[i].Name);
                sb.Append(TypeRefEmit.EmitParam(sig.Params[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(sig.Params[i].Name));
            }
            sb.Append(")\n");
            sb.Append("        {\n");
            sb.Append("            var result =  ").Append(stubClass).Append(".wasmImport").Append(methodName);
            sb.Append('(');
            for (int i = 0; i < sig.Params.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var argName = NameConventions.ToCamelCase(sig.Params[i].Name);
                if (sig.Params[i].Type is CtPrimType p)
                    sb.Append(PrimMarshal.Lower(p.Kind, argName));
                else
                    sb.Append(argName);
            }
            sb.Append(");\n");

            if (returnsSelf)
            {
                sb.Append("            var resource = new ").Append(qualifiedClass);
                sb.Append("(new ").Append(qualifiedClass).Append(".THandle(result));\n");
                sb.Append("            return resource;\n");
            }
            else if (!sig.HasNoResult && sig.Result != null)
            {
                sb.Append("            return ");
                if (sig.Result is CtPrimType rp)
                    sb.Append(PrimMarshal.Lift(rp.Kind, "result"));
                else
                    sb.Append("result");
                sb.Append(";\n");
            }

            sb.Append("\n            //TODO: free alloc handle (interopString) if exists\n");
            sb.Append("        }\n\n");
        }

        /// <summary>True if the signature returns an own&lt;Self&gt; handle.</summary>
        private static bool ReturnsSelfResource(CtFunctionType sig,
                                                string resourceWitName)
        {
            if (sig.Result is CtTypeRef r && r.Name == resourceWitName)
                return true;
            if (sig.Result is CtOwnType own && own.Resource is CtTypeRef r2
                && r2.Name == resourceWitName)
                return true;
            return false;
        }

        /// <summary>
        /// Build the fully qualified <c>global::</c>-prefixed type
        /// name for a resource class. Used in static methods that
        /// return the resource itself — wit-bindgen qualifies the
        /// reference even when emitting code inside the class.
        /// </summary>
        private static string QualifiedResourceTypeName(CtInterfaceType owner,
                                                        string className)
        {
            // NOTE: direction is hardcoded to "imports". This works
            // for the current test fixtures (all imports); a
            // complete implementation threads world direction
            // through the emitter (see the cross-interface
            // qualifying follow-up).
            var sb = new StringBuilder("global::");
            // World namespace is not yet plumbed into TypeDefEmit;
            // reconstruct from the owner's package. We use a
            // conservative placeholder: "XWorld" is ambiguous, so
            // for now use an explicit emitter-local context.
            sb.Append(WorldNamespaceForOwner(owner));
            sb.Append(".wit.imports.");
            sb.Append(owner.Package!.Namespace);
            foreach (var seg in owner.Package.Path)
            {
                sb.Append('.');
                sb.Append(seg);
            }
            var v = NameConventions.SanitizeVersion(owner.Package.Version);
            if (v.Length > 0) sb.Append('.').Append(v);
            sb.Append(".I").Append(NameConventions.ToPascalCase(owner.Name));
            sb.Append('.').Append(className);
            return sb.ToString();
        }

        /// <summary>
        /// Reconstruct the world namespace for an interface owner.
        /// Uses ambient thread-local state set by the top-level
        /// emit entry (see <see cref="EmitAmbient"/>).
        /// </summary>
        private static string WorldNamespaceForOwner(CtInterfaceType _owner) =>
            EmitAmbient.WorldNamespace ?? "UNSET_WORLD_NAMESPACE";

        private static void EmitResourceMethod(StringBuilder sb,
                                               CtResourceMethod m,
                                               string resourceWitName,
                                               string entryPointBase)
        {
            var methodName = NameConventions.ToPascalCase(m.Name!);
            var stubClass = methodName + "WasmInterop";
            var sig = m.Function;
            var usesReturnArea = MethodUsesReturnArea(sig);

            // Stub class
            sb.Append("        internal static class ").Append(stubClass).Append('\n');
            sb.Append("        {\n");
            sb.Append("            [DllImport(\"").Append(entryPointBase);
            sb.Append("\", EntryPoint = \"")
              .Append(EntryPoints.ResourceMethod(resourceWitName, m.Name!))
              .Append("\"), WasmImportLinkage]\n");

            sb.Append("            internal static extern ");
            // For return-area methods the stub returns void; the
            // actual value comes back via the trailing nint param.
            sb.Append(usesReturnArea ? "void" : StubReturnType(sig));
            sb.Append(" wasmImport").Append(methodName).Append("(int p0");
            // Multi-slot params (list<u8>, string, option<...>, etc.)
            // contribute more than one stub slot; use StubTypesFor to
            // flatten each param into its core-wasm slot types. The
            // stub-index counter advances across flattened slots.
            int stubIdx = 1;
            for (int i = 0; i < sig.Params.Count; i++)
            {
                foreach (var slotTy in InteropEmit.StubTypesFor(sig.Params[i].Type))
                {
                    sb.Append(", ").Append(slotTy).Append(" p").Append(stubIdx);
                    stubIdx++;
                }
            }
            if (usesReturnArea)
            {
                sb.Append(", nint p").Append(stubIdx);
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
                FunctionEmit.EmitParamWitName(sb, sig.Params[i].Name);
                sb.Append(TypeRefEmit.EmitParam(sig.Params[i].Type));
                sb.Append(' ');
                sb.Append(NameConventions.ToCamelCase(sig.Params[i].Name));
            }
            sb.Append(")\n");
            sb.Append("        {\n");
            sb.Append("            var handle = this.Handle;\n");

            var retType = FunctionEmit.EmitReturnType(sig);
            var isVoid = retType == "void";
            var hasPrelude = InteropEmit.HasPrelude(sig);

            // Resource-ref params: `var handle{N} = arg.Handle;`
            // (+ zero-out for own). Self already took slot 0, so
            // per-param slots start at 1 — first resource-ref param
            // is `handle0`, second is `handle1`, etc.
            InteropEmit.EmitResourceRefHandles(sb, sig, startingSlot: 1);

            // Aggregate-param prelude (stackalloc for list<T>,
            // InteropString.FromString for string, option lowering
            // branches). Shared with the free-function path in
            // InteropEmit so the shape is identical. wit-bindgen
            // inserts a blank line between `var handle = this.Handle;`
            // and the prelude — match it.
            if (hasPrelude)
            {
                sb.Append("\n");
                InteropEmit.EmitWrapperPrelude(sb, sig);
            }

            if (usesReturnArea)
            {
                // Blank separator before the retArea block matches
                // the wit-bindgen shape for resource methods (the
                // handle declaration just above is separated from
                // the body).
                sb.Append("\n");
                InteropEmit.EmitReturnAreaWrapperBody(
                    sb, stubClass, methodName, sig, leadingStubArg: "handle",
                    resHandleStartingSlot: 1);
            }
            else if (InteropEmit.IsElidedResultType(sig.Result))
            {
                // result<_, _> — stub returns i32 discriminant
                // directly; capture into `result` then emit the
                // shared switch + throw body.
                sb.Append("            var result =  ");
                sb.Append(stubClass).Append(".wasmImport").Append(methodName);
                sb.Append("(handle");
                if (sig.Params.Count > 0)
                {
                    sb.Append(", ");
                    InteropEmit.EmitLoweredArgs(sb, sig, resHandleStartingSlot: 1);
                }
                sb.Append(");\n");
                InteropEmit.EmitElidedResultTail(sb);
            }
            else
            {
                // Stub call — resource-method shape: first arg is
                // always `handle`, then per-param lowered args.
                // wit-bindgen quirk: double-space after `=` in
                // `var result =  …` for non-void.
                sb.Append("            ");
                if (!isVoid) sb.Append("var result =  ");
                sb.Append(stubClass).Append(".wasmImport").Append(methodName);
                sb.Append("(handle");
                if (sig.Params.Count > 0)
                {
                    sb.Append(", ");
                    InteropEmit.EmitLoweredArgs(sb, sig, resHandleStartingSlot: 1);
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
            }

            sb.Append("\n");
            sb.Append("            //TODO: free alloc handle (interopString) if exists\n");
            sb.Append("        }\n\n");
        }

        private static string PrimStubType(CtValType t)
        {
            if (t is CtPrimType p) return PrimMarshal.StubType(p.Kind);
            // Resource-typed returns (static factories returning a
            // fresh handle of the resource) lower to int — the
            // wasm-side handle representation.
            if (t is CtTypeRef || t is CtOwnType) return "int";
            throw new System.NotImplementedException(
                "Resource stub type for " + t.GetType().Name +
                " is a follow-up.");
        }

        private static string StubReturnType(CtFunctionType sig)
        {
            if (sig.HasNoResult) return "void";
            if (sig.Result == null) return "void";
            // Totally-elided result<_,_> — stub returns the
            // discriminant directly as i32.
            if (sig.Result is CtResultType r
                && r.Ok == null && r.Err == null) return "int";
            return PrimStubType(sig.Result);
        }

        /// <summary>
        /// True when a resource method needs a return-area buffer.
        /// Delegates to <see cref="InteropEmit.UsesReturnArea"/> so
        /// resource methods transparently pick up every aggregate
        /// return shape the free-function emitter supports.
        /// </summary>
        private static bool MethodUsesReturnArea(CtFunctionType sig) =>
            InteropEmit.UsesReturnArea(sig);


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
