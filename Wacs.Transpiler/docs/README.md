# Wacs.Transpiler AOT Documentation

These four documents are the design touchstone for the AOT transpiler. Read them before modifying emission code. Update them when a pattern is missing.

1. **[01-wasm-to-cil-mapping.md](01-wasm-to-cil-mapping.md)** — Authoritative reference for how every WASM concept (value types, stack, control flow, calls, memory, tables, globals, GC, refs, exceptions) maps to CIL. Contract for all implementation decisions.

2. **[02-structural-patterns.md](02-structural-patterns.md)** — Named patterns used to bridge WASM semantics and CIL's stricter rules. Each pattern has a stated problem, solution shape, and invariants implementations must preserve.

3. **[03-validation-layers.md](03-validation-layers.md)** — Responsibility split between the WASM validator (Tier A, pre-transpile), `CilValidator` (Tier B, emit-time CIL type tracking), and runtime trap checks (Tier C, emitted IL). What the transpiler can assume vs must verify.

4. **[04-implementation-plan.md](04-implementation-plan.md)** — Staged work plan derived from docs 1–3. Entry/exit criteria per stage; no one-off hacks.

## Working discipline

- **Code follows docs.** If a pattern isn't written down, add it to doc 2 before implementing.
- **Equivalence, not test counts, is the gate.** The spec test suite is re-enabled as a gate only at stage 4 of doc 4.
- **Each change points to a doc section** in its commit message or PR description.
