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
