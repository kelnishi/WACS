// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.Core.Text
{
    /// <summary>
    /// Output style for the consolidated text emitter. The default
    /// (<see cref="TextWriterStyle.Canonical"/>) produces parser-
    /// friendly flat WAT — what <c>TextModuleWriter</c> always emitted
    /// before consolidation. The other modes add layers on top.
    /// </summary>
    public enum TextWriterStyle
    {
        /// <summary>Canonical flat WAT. Instructions are emitted one
        /// per line, indented inside their block / function. Re-parses
        /// to a structurally equivalent <see cref="Module"/>.</summary>
        Canonical = 0,

        /// <summary>
        /// Canonical flat WAT plus debug annotations: per-function
        /// id comment, <c>;; label = @N</c> markers at block heads, and
        /// left-margin stack-state annotations on each instruction.
        /// Useful for diagnostic dumps (the CLI's validation-error
        /// reporter routes through here). Output re-parses, but the
        /// extra `;; …` comments are tokenizer-discarded today.
        /// </summary>
        StackAnnotated = 1,

        /// <summary>
        /// Folded / S-expression form. Future pass (#5) — placeholder
        /// for now; emitting this style falls back to
        /// <see cref="Canonical"/> until the per-opcode arity folder
        /// lands.
        /// </summary>
        Folded = 2,
    }

    /// <summary>
    /// Options bag for <c>TextModuleWriter</c>. New knobs land here
    /// rather than as method-parameter explosions on the public
    /// <c>Write*</c> entry points.
    /// </summary>
    public sealed class TextWriterOptions
    {
        public static readonly TextWriterOptions Canonical =
            new() { Style = TextWriterStyle.Canonical };

        public static readonly TextWriterOptions StackAnnotated =
            new() { Style = TextWriterStyle.StackAnnotated };

        public TextWriterStyle Style { get; set; } = TextWriterStyle.Canonical;
    }
}
