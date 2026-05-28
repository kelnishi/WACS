// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.ComponentModel.Async;

namespace WitHarnessSpike.Aot
{
    /// <summary>
    /// Source-generator-driven equivalent of the hand-written
    /// <see cref="HelloHarness"/>. The
    /// <c>AsyncComponentHarnessGenerator</c> emits the
    /// constructor + lazy invoker fields + the
    /// <c>StringCoding.LowerUtf8</c> + retArea-lift body for
    /// the <see cref="Greet"/> method. End-to-end NativeAOT
    /// verification that the generator's string-marshaling
    /// codegen matches the hand-written pattern.
    /// </summary>
    [AsyncComponentHarness]
    public partial class GeneratedHelloHarness
    {
        [SyncExport("greet")]
        public partial string Greet(string name);
    }
}
