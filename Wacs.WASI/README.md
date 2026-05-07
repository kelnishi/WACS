# Wacs.WASI

Host implementations of the WebAssembly System Interface, organized by
sub-family. Each sub-folder is its own self-contained NuGet group —
embedders depending on Preview 1 don't pull Preview 2's transitives,
and so on.

## Contents

- **[Wacs.WASI.NN/](Wacs.WASI.NN/)** — wasi-nn (`wasi:nn@0.2.0-rc-2024-10-28` WIT + legacy WITX). Backend-agnostic core + sibling NuGets for the three backends (ML.NET, ONNX Runtime, LlamaSharp/GGUF).
- **[Wacs.WASI.Preview1/](Wacs.WASI.Preview1/)** — WASI Preview 1 host (`wasi_snapshot_preview1`) covering filesystem / clocks / random / sockets / proc / poll. Includes the deprecated `WACS.WASIp1` metapackage that forwards to the current Preview 1 NuGet.
- **[Wacs.WASI.Preview2/](Wacs.WASI.Preview2/)** — WASI Preview 2 component-model host pinned at WASI 0.2.3 (cli / clocks / filesystem / http / io / random / sockets, end-to-end) plus a `Microsoft.Extensions.DependencyInjection` extension package.
- **[Wacs.WASI.Threads/](Wacs.WASI.Threads/)** — `wasi-threads` proposal: `wasi_thread_spawn` + the shared-memory threading model on top of `Wacs.Core`'s built-in atomics / `wait`/`notify`.
