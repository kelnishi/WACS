// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// v1 phase 3 (in progress): the Webgpu DI sibling assembly lands
// in a follow-up session. Once it exists this becomes:
//   [assembly: Wacs.ComponentModel.Runtime
//       .WacsDependencyInjectionSibling(
//           "Wacs.WASI.GFX.Webgpu.DependencyInjection")]
// and HostPackageResolver auto-loads it before the [WitSource]
// scan. Kept here as a placeholder so the wiring is obvious to a
// future reader.
