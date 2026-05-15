// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Runtime;

// Declares the DI sibling assembly that holds the SourceGen-shape
// resource impl classes (Context, AbstractBuffer, Surface, Device,
// Buffer) and the WasiGfxBundle + WasiPreview2GfxBundle composite.
// HostPackageResolver + WasiPreview2RuntimeScope read this attribute
// on first scan of Wacs.WASI.GFX and Assembly.Load() the sibling —
// no need for the IBindable or the CLI to know the sibling's name.
[assembly: WacsDependencyInjectionSibling(
    "Wacs.WASI.GFX.DependencyInjection")]

// v1 phase 1 1d: declares the WIT packages this assembly owns
// (graphics-context + frame-buffer + surface). Downstream
// packages like Wacs.WASI.GFX.Webgpu read this attribute and
// route their `use wasi:graphics-context/...` cross-package
// references to Wacs.WASI.GFX automatically.
[assembly: WitPackageMapping("wasi:graphics-context", "Wacs.WASI.GFX")]
[assembly: WitPackageMapping("wasi:frame-buffer", "Wacs.WASI.GFX")]
[assembly: WitPackageMapping("wasi:surface", "Wacs.WASI.GFX")]
