// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

// Declares the DI sibling assembly that holds the WasiPreview2Bundle
// (the base bundle every wasip2 run resolves) plus the Preview2
// resource impl classes. HostPackageResolver +
// WasiPreview2RuntimeScope read this attribute on first scan of
// Wacs.WASI.Preview2 and Assembly.Load() the sibling.
[assembly: WacsDependencyInjectionSibling(
    "Wacs.WASI.Preview2.DependencyInjection")]
