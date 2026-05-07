# Wacs.Bench

Developer-only perf harnesses. None of these ship to NuGet — they're
research / regression-tracking executables run from the repo root via
`dotnet run --project ...`.

## Contents

- **[Wacs.Bench/](Wacs.Bench/)** — micro-benchmark dispatcher for the polymorphic + switch interpreters; cold-start profiler with per-phase breakdown (parse / inst / xpile / activate / first / warm).
- **[Wacs.Bench.Aot/](Wacs.Bench.Aot/)** — AOT-published variant of the bench; demonstrates a NativeAOT consumer that references only `Wacs.Core` (no Reflection.Emit).
- **[Wacs.OpProfile/](Wacs.OpProfile/)** — opcode-frequency profiler over real workloads (coremark, wasm2wat, perl, …); emits `/tmp/opprofile.tsv` to inform Switch Runtime hot/cold partitioning decisions.
