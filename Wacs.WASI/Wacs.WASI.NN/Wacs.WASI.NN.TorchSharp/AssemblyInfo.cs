// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.HostBindings.Abstractions;

// Marks this assembly as a WACS host package — discoverable via
// `runtime.AutoDiscoverHostPackages()` once the assembly is loaded
// into the AppDomain. The package's WasiNNTorchSharpBindable
// adapter has a parameterless ctor, so the auto-discovery path
// activates and binds it without requiring the embedder to
// enumerate package names by hand.
[assembly: WasiHostPackage("WASI Neural Network — TorchSharp / PyTorch backend")]
