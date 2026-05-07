# Doc 5 — Performance & Polish Icebox

**Prerequisite:** docs 1–4 complete, suite green.

This is the post-correctness icebox. Each item is an opportunistic
optimization once the transpiler is spec-equivalent to the interpreter.
None of these are scheduled — pull one when a profile or a user report
points at it.

**Ground rule:** every optimization must ship with a measurement and
an off-switch (a `TranspilerOptions` flag or equivalent). Correctness
remains the gate.

---

## Candidates

### Castclass elision

Inline `Castclass` sites where the source is provably of the target
type — skip the runtime check. Specifically: after a successful
`ref.cast`, downstream `struct.get` / `array.get` that cast the same
object to the same CLR type can drop the `castclass` and just `ldfld`.
Requires a lightweight per-instruction flow analysis that tracks the
"known type" of each stack slot within a basic block.

### Null-check elision

Avoid redundant null checks when a value was just produced by a non-null
operation. `ref.as_non_null` guarantees non-null; a following
`struct.get` / `array.get` / `call_ref` in the same block can skip its
own null guard. Same flow-analysis shape as castclass elision.

### `call_indirect` specialization

When a funcref table has a single known function type (common in
practice), skip the sub-supertype hash walk and compare only the stored
hash directly. Requires a transpile-time scan of all tables to detect
the single-type case.

### `ref.test` specialization

When the target type is a final concrete type (no subtypes), only
Layer 0 (CLR `IsInstanceOfType`) is needed — skip the Layer 1 structural
hash walk entirely. Detect "final" at transpile time by checking the
subtype tree doesn't branch.

### Boundary wrap/unwrap peephole

Reduce redundant `WrapRef` / `UnwrapRef` at chained boundaries. Common
pattern: `table.get idx` immediately followed by `table.set idx` — the
wrap-to-Value-then-unwrap is pure overhead. A peephole that matches
adjacent wrap/unwrap with matching type can drop both. Similar for
`global.get` → `global.set` passthrough and argument reorderings around
`call`.

---

## Exit criteria (per item)

- Before/after benchmarks against the interpreter and against the
  pre-optimization transpiler output.
- Correctness gate: full spec suite (473/473 as of doc 4 session 4)
  stays green with the optimization both on and off.
- Off-switch documented in `TranspilerOptions`.
- If the optimization doesn't produce measurable improvement on a
  representative workload, drop it — don't ship complexity that
  doesn't pay.
