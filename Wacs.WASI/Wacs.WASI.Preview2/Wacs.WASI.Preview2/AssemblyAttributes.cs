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

// v1 phase 1 1d: declares which WIT packages this assembly owns
// the generated host interfaces for. Downstream packages with
// `use wasi:io/poll@X.{ pollable }` in their WIT get the
// IPollable cross-package reference routed to
// Wacs.WASI.Preview2.Io.IPollable automatically — no manual
// <WitHostPackageNamespaceMap> override needed in their csproj.
//
// Multiple attrs per assembly cover every WIT package
// Wacs.WASI.Preview2 emits host interfaces for.
[assembly: WitPackageMapping("wasi:io", "Wacs.WASI.Preview2")]
[assembly: WitPackageMapping("wasi:cli", "Wacs.WASI.Preview2")]
[assembly: WitPackageMapping("wasi:filesystem", "Wacs.WASI.Preview2")]
[assembly: WitPackageMapping("wasi:clocks", "Wacs.WASI.Preview2")]
[assembly: WitPackageMapping("wasi:random", "Wacs.WASI.Preview2")]
[assembly: WitPackageMapping("wasi:sockets", "Wacs.WASI.Preview2")]
[assembly: WitPackageMapping("wasi:http", "Wacs.WASI.Preview2")]
