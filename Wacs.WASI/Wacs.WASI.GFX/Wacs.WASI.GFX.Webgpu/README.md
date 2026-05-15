# WACS.WASI.GFX.Webgpu

WASI-GFX `wasi:webgpu@0.0.1` host bindings for WACS. The fourth
wasi-gfx WIT package, mirroring the browser WebGPU spec verbatim.

This is the **contract assembly** — `[WitSource]`-tagged interfaces
(generated from `wit/webgpu.wit`) plus the WitBindings dispatcher
that ties them into the WACS runtime. The actual GPU backend lives
in `WACS.WASI.GFX.Silk` and wraps `Silk.NET.WebGPU` /
[wgpu-native](https://github.com/gfx-rs/wgpu-native).

## Status

v1 in progress. See `docs/wasi-gfx-v1-plan.md` Phase 3 for the
chunked roadmap.
