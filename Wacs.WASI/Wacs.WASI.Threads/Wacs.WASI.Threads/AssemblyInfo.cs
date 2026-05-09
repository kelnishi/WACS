// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.HostBindings.Abstractions;

// `WasiThreads : IBindable` ships a parameterless ctor and is
// already discoverable through `--bind Wacs.WASI.Threads`. The
// [WasiHostPackage] marker adds the assembly to the AppDomain-
// scan path that `runtime.AutoDiscoverHostPackages()` walks.
[assembly: WasiHostPackage("WASI Threads — wasi:thread-spawn")]
