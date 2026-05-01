// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using CommandLine;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Flags shared by every verb (run / build / inspect). Verb POCOs
    /// pull these in via composition rather than inheritance —
    /// CommandLineParser's <c>[Verb]</c> attribute requires concrete
    /// classes, and inherited <c>[Option]</c>s only flow through
    /// when the parser sees them on the leaf class.
    /// </summary>
    public abstract class SharedOptions
    {
        // Note: positional [Value] declarations must live on the leaf
        // verb class — CommandLineParser doesn't always pick them up
        // reliably from a base class. Each verb declares its own
        // `Files` Value(0) accordingly.

        [Option("no-validate", HelpText =
            "Skip module validation after parsing. By default the "
            + "validator surfaces malformed-module errors with .wat "
            + "renders of the offending function bodies.")]
        public bool NoValidate { get; set; }

        [Option('v', "verbose", HelpText =
            "Print parser timing, instantiation diagnostics, and a "
            + "transpilation summary when applicable.")]
        public bool Verbose { get; set; }
    }
}
