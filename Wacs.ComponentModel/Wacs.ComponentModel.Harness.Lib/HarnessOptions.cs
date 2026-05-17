// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// Tunables for the harness emit pass. All fields have sensible
    /// defaults — most callers pass <c>null</c> and let the emitter
    /// derive everything from the WIT input.
    /// </summary>
    public sealed class HarnessOptions
    {
        /// <summary>
        /// CLR namespace for the emitted harness class (and the
        /// symmetric interface, when interface emission lands). Default:
        /// <c>Wacs.ComponentModel.Harness.Generated</c>.
        /// </summary>
        public string Namespace { get; set; } = "Wacs.ComponentModel.Harness.Generated";

        /// <summary>
        /// Assembly + simple-name for the emitted .dll. Default: derived
        /// from the WIT world name (PascalCase + "Harness", e.g.
        /// <c>HelloHarness</c>).
        /// </summary>
        public string? AssemblyName { get; set; }
    }
}
