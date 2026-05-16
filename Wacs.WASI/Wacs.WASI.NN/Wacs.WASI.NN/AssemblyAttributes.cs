// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

// Declares the DI sibling assembly that holds the SourceGen-shape
// resource impl classes (Tensor, Graph, GraphExecutionContext) and
// the WasiNNBundle + WasiPreview2NNBundle composite.
// HostPackageResolver + WasiPreview2RuntimeScope read this attribute
// on first scan of Wacs.WASI.NN and Assembly.Load() the sibling —
// no need for the backend bindable or the CLI to know the sibling's
// name.
[assembly: WacsDependencyInjectionSibling(
    "Wacs.WASI.NN.DependencyInjection")]

// v1 phase 1 1d: wasi:nn types live in Wacs.WASI.NN.
[assembly: WitPackageMapping("wasi:nn", "Wacs.WASI.NN")]
