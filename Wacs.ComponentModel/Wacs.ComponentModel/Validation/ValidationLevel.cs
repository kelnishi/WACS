// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Validation
{
    /// <summary>
    /// Severity level for binding-against-WIT validation. Set on
    /// the <see cref="Linker"/> at construction time; chosen by
    /// consumers based on their tolerance for runtime mismatch
    /// detection cost.
    /// </summary>
    public enum ValidationLevel
    {
        /// <summary>No validation. Bindings register; the
        /// runtime traps at invoke time if a host method is
        /// missing or shape-mismatched. Cheapest — matches the
        /// pre-validation behavior.</summary>
        Off = 0,

        /// <summary>Validation runs and reports issues, but
        /// only logs/collects them. The
        /// <see cref="Linker.Validate"/> call returns the
        /// report; the consumer decides what to do.</summary>
        Warnings = 1,

        /// <summary>Validation runs; first issue
        /// throws <see cref="ValidationException"/>. Use to
        /// fail-fast at link time before the component runs —
        /// surfaces contract drift as a deterministic exception
        /// rather than a wasm trap deep into execution.</summary>
        Strict = 2,
    }
}
