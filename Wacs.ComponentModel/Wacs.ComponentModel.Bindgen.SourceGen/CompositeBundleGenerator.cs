// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Wacs.ComponentModel.Bindgen.SourceGen
{
    /// <summary>
    /// Roslyn incremental source generator: finds partial classes
    /// tagged with <c>[WacsCompositeBundleGen(typeof(Base),
    /// typeof(Sibling), ...)]</c> and emits the matching partial
    /// half — a constructor + forwarding properties for every
    /// public read-only property on each underlying bundle +
    /// direct accessors for both sides + the
    /// <c>[WacsCompositeBundle]</c> family tag.
    ///
    /// <para>v1 phase 1 deferred item 1h. Replaces the hand-
    /// written <c>WasiPreview2GfxBundle</c> +
    /// <c>WasiPreview2NNBundle</c> boilerplate (each ~90 lines)
    /// with single-line partial-class declarations. N-way
    /// composites for guests that import multiple sibling
    /// families (e.g. Preview2 + GFX + NN + Webgpu) become
    /// trivial — one more partial declaration per combination.
    /// </para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class CompositeBundleGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var marker = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "Wacs.ComponentModel.Runtime.WacsCompositeBundleGenAttribute",
                    static (node, _) =>
                        node is ClassDeclarationSyntax,
                    static (gac, _) => Decode(gac))
                .Where(static info => info != null)
                .Select(static (info, _) => info!);

            context.RegisterSourceOutput(marker,
                static (spc, info) => Emit(spc, info));
        }

        private sealed class CompositeInfo
        {
            public INamedTypeSymbol Composite { get; set; } = null!;
            public INamedTypeSymbol Base { get; set; } = null!;
            public INamedTypeSymbol Sibling { get; set; } = null!;
            public string Family { get; set; } = "";
            public int Priority { get; set; } = 10;
            public string BaseAccessor { get; set; } = "Base";
            public string SiblingAccessor { get; set; } = "Sibling";
        }

        private static CompositeInfo? Decode(
            GeneratorAttributeSyntaxContext gac)
        {
            if (gac.TargetSymbol is not INamedTypeSymbol cls) return null;
            if (gac.Attributes.Length == 0) return null;
            var attr = gac.Attributes[0];
            if (attr.ConstructorArguments.Length < 2) return null;
            var baseType = attr.ConstructorArguments[0].Value
                as INamedTypeSymbol;
            var siblingType = attr.ConstructorArguments[1].Value
                as INamedTypeSymbol;
            if (baseType == null || siblingType == null) return null;

            string family = "";
            int priority = 10;
            string baseAccessor = "Base";
            string siblingAccessor = "Sibling";
            foreach (var na in attr.NamedArguments)
            {
                switch (na.Key)
                {
                    case "Family":
                        family = (na.Value.Value as string) ?? "";
                        break;
                    case "Priority":
                        if (na.Value.Value is int i) priority = i;
                        break;
                    case "BaseAccessor":
                        baseAccessor = (na.Value.Value as string)
                            ?? "Base";
                        break;
                    case "SiblingAccessor":
                        siblingAccessor = (na.Value.Value as string)
                            ?? "Sibling";
                        break;
                }
            }
            return new CompositeInfo
            {
                Composite = cls,
                Base = baseType,
                Sibling = siblingType,
                Family = family,
                Priority = priority,
                BaseAccessor = baseAccessor,
                SiblingAccessor = siblingAccessor,
            };
        }

        // Display format for fully-qualified type names with
        // nullable reference type annotations preserved — matches
        // what a C# author would write in source.
        private static readonly SymbolDisplayFormat TypeFormat
            = SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(
                    SymbolDisplayMiscellaneousOptions
                        .IncludeNullableReferenceTypeModifier
                    | SymbolDisplayMiscellaneousOptions
                        .UseSpecialTypes);

        private static void Emit(SourceProductionContext spc,
            CompositeInfo info)
        {
            var ns = info.Composite.ContainingNamespace.IsGlobalNamespace
                ? null
                : info.Composite.ContainingNamespace
                    .ToDisplayString();
            string compositeName = info.Composite.Name;
            string baseTy = info.Base.ToDisplayString(TypeFormat);
            string siblingTy = info.Sibling.ToDisplayString(TypeFormat);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// Emitted by Wacs.ComponentModel.Bindgen");
            sb.AppendLine("//   .SourceGen.CompositeBundleGenerator");
            sb.AppendLine("// from the [WacsCompositeBundleGen]");
            sb.AppendLine("// attribute on the partial class.");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            if (ns != null)
            {
                sb.Append("namespace ").Append(ns).AppendLine();
                sb.AppendLine("{");
            }
            // Stamp the generated partial with the
            // [WacsCompositeBundle(family, Priority = N)] discovery
            // attribute so the resolver + runtime scope find it.
            sb.Append("    [global::Wacs.ComponentModel.Runtime")
              .Append(".WacsCompositeBundle(\"")
              .Append(EscapeString(info.Family))
              .Append("\", Priority = ")
              .Append(info.Priority)
              .AppendLine(")]");
            sb.Append("    partial class ")
              .AppendLine(compositeName);
            sb.AppendLine("    {");

            // Private fields.
            sb.Append("        private readonly ").Append(baseTy)
              .AppendLine(" _baseBundle;");
            sb.Append("        private readonly ").Append(siblingTy)
              .AppendLine(" _siblingBundle;");
            sb.AppendLine();

            // Constructor — null-checks both bundles.
            sb.Append("        public ").Append(compositeName)
              .AppendLine("(");
            sb.Append("            ").Append(baseTy)
              .AppendLine(" baseBundle,");
            sb.Append("            ").Append(siblingTy)
              .AppendLine(" siblingBundle)");
            sb.AppendLine("        {");
            sb.AppendLine("            _baseBundle = baseBundle");
            sb.AppendLine("                ?? throw new ArgumentNullException(nameof(baseBundle));");
            sb.AppendLine("            _siblingBundle = siblingBundle");
            sb.AppendLine("                ?? throw new ArgumentNullException(nameof(siblingBundle));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Direct accessors.
            sb.Append("        public ").Append(baseTy)
              .Append(' ').Append(info.BaseAccessor)
              .AppendLine(" => _baseBundle;");
            sb.Append("        public ").Append(siblingTy)
              .Append(' ').Append(info.SiblingAccessor)
              .AppendLine(" => _siblingBundle;");
            sb.AppendLine();

            // Forwarders for base properties.
            var seenNames = new HashSet<string>();
            seenNames.Add(info.BaseAccessor);
            seenNames.Add(info.SiblingAccessor);
            EmitForwarders(sb, "_baseBundle", info.Base, seenNames);
            EmitForwarders(sb, "_siblingBundle", info.Sibling,
                seenNames);

            sb.AppendLine("    }");
            if (ns != null) sb.AppendLine("}");

            string fileName = compositeName + ".CompositeBundleGen.g.cs";
            spc.AddSource(fileName,
                SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private static void EmitForwarders(StringBuilder sb,
            string fieldRef, INamedTypeSymbol type,
            HashSet<string> seenNames)
        {
            foreach (var prop in GetPublicReadable(type))
            {
                // Skip duplicate-name collisions: if the base
                // and sibling both expose `Label`, the second
                // forwarder would be a compile error. First one
                // wins; future authors can hand-write a manual
                // forwarder on the partial half for the loser.
                if (!seenNames.Add(prop.Name)) continue;
                sb.Append("        public ")
                  .Append(prop.Type.ToDisplayString(TypeFormat))
                  .Append(' ').Append(prop.Name)
                  .Append(" => ").Append(fieldRef)
                  .Append('.').Append(prop.Name)
                  .AppendLine(";");
            }
        }

        private static IEnumerable<IPropertySymbol>
            GetPublicReadable(INamedTypeSymbol type)
        {
            // Walk the type AND its base types (excluding object)
            // so a composite forwarding a derived bundle still
            // picks up base-class properties. Skip indexers — the
            // forwarding shape only fits plain properties.
            for (var t = type; t != null && t.SpecialType
                    != SpecialType.System_Object; t = t.BaseType)
            {
                foreach (var m in t.GetMembers().OfType<IPropertySymbol>())
                {
                    if (m.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    if (m.IsIndexer) continue;
                    if (m.GetMethod == null) continue;
                    if (m.GetMethod.DeclaredAccessibility
                        != Accessibility.Public) continue;
                    if (m.IsStatic) continue;
                    yield return m;
                }
            }
        }

        private static string EscapeString(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}
