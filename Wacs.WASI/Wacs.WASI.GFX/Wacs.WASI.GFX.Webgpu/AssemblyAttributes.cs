// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

// wasi:webgpu host interfaces live in this assembly. Packages
// that `use wasi:webgpu/...` route here automatically. When a
// Wacs.WASI.GFX.Webgpu.DependencyInjection sibling assembly
// exists, add the matching WacsDependencyInjectionSibling
// attribute so HostPackageResolver auto-loads it.
[assembly: WitPackageMapping("wasi:webgpu", "Wacs.WASI.GFX.Webgpu")]
