// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Embeds the source WIT text that produced a generated host
    /// interface, method, or type. The validation layer reads
    /// these to compare a runtime binding's contract against an
    /// expected WIT spec — the binding can either be validated
    /// against an externally-supplied WIT, or the embedded WIT
    /// can be extracted to reconstruct the contract the binding
    /// was generated from.
    ///
    /// <para>Applied at three granularities:
    /// <list type="bullet">
    /// <item>Interface-level: the full <c>interface foo { ... }</c>
    /// declaration text (or the <c>resource X { ... }</c> body).
    /// <see cref="Package"/> + <see cref="Interface"/> identify
    /// the WIT-side namespace.</item>
    /// <item>Method-level: a single <c>name: func(...) -&gt; ...</c>
    /// line. The interface attribute supplies the surrounding
    /// context.</item>
    /// <item>Type-level: a record / variant / enum / flags / type
    /// alias declaration text.</item>
    /// </list></para>
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Interface | AttributeTargets.Method
        | AttributeTargets.Class | AttributeTargets.Struct
        | AttributeTargets.Enum,
        AllowMultiple = false, Inherited = false)]
    public sealed class WitSourceAttribute : Attribute
    {
        /// <summary>The verbatim WIT-text fragment (may include
        /// trailing newline; consumers should trim if comparing
        /// against parser output).</summary>
        public string Source { get; }

        /// <summary>Fully-qualified WIT package, e.g.
        /// <c>"wasi:cli@0.2.3"</c>. Null when the attribute
        /// applies to a type alias not anchored in a package
        /// (rare).</summary>
        public string? Package { get; set; }

        /// <summary>Local interface name (kebab-case), e.g.
        /// <c>"environment"</c> or <c>"types"</c>. Null on
        /// world-level / package-level attributes.</summary>
        public string? Interface { get; set; }

        /// <summary>Local item name (kebab-case) within the
        /// interface — function name, resource name, type-alias
        /// name. Null on interface-level attributes.</summary>
        public string? Item { get; set; }

        public WitSourceAttribute(string source)
        {
            Source = source ?? throw new ArgumentNullException(
                nameof(source));
        }
    }
}
