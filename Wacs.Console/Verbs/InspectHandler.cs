// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Phase 1 stub. Phase 3 implements: parse via
    /// <see cref="Wacs.Core.BinaryModuleParser.ParseWasm"/> /
    /// ComponentBinaryParser.Parse, render WAT via
    /// <see cref="Wacs.Core.Text.TextModuleWriter"/>, and walk the
    /// parsed module for stats / exports / imports.
    /// </summary>
    public static class InspectHandler
    {
        public static int Execute(InspectOptions opts)
        {
            System.Console.Error.WriteLine(
                "wacs inspect: not yet implemented in this build "
                + "(skeleton phase).");
            return 2;
        }
    }
}
