# Multi-Core Component Instantiation — Spec-Proper Plan

## Why

`ComponentInstance.InstantiateMultiCore` currently only
instantiates the **primary** core module. The shim and fixup
core modules wit-component emits are parsed but never
instantiated. That works for cli-hello (its async body
completes synchronously through canon-lower returns and never
calls `call_indirect` on the shim's funcref table) but breaks
for any fixture whose body actually exercises the shim
indirection — every async `descriptor.<method>().await`,
every async filesystem / sockets / http call.

This is the same observation from `project-wit-component-shim-slot-map`:
the slot→qualified-import map only lives in the
component-level aliases + inline-export bundles, not in the
shim core module's function-name section, and the funcref
table is filled by the fixup module's element segment at
fixup-instantiation time.

## What the spec requires

Per the Component Model spec, when a component instantiates,
it walks its sections in file order, maintaining parallel
index spaces:

- core-module-idx → the parsed core binaries
- core-instance-idx → either a real `ModuleInstance` (from
  `InstantiateCoreModule`) or a virtual bundle of named
  externals (from `InstantiateCoreInline`)
- core-func-idx / core-table-idx / core-memory-idx /
  core-global-idx → items aliased from instances OR produced
  by `canon` entries
- component-level index spaces (instance, func, type, …)

Section processing:

| Section | Effect on core spaces |
|---|---|
| `core-module` | adds binary to core-module space |
| `core-instance.InstantiateCoreModule(M, args)` | walks `args = [(name, instance-idx)]`, sets up host imports `(name, exportName)` for each export of the source instance, calls `InstantiateModule(M)`, stores `ModuleInstance` |
| `core-instance.InstantiateCoreInline(exports)` | builds virtual instance: `{name → (sort, idx)}` resolved via current core spaces |
| `core-alias.InstanceExport(coreSort, instIdx, name)` | promotes named export of `instIdx` into the appropriate core space at the next index slot |
| `canon-lower(componentFuncIdx)` | produces a core-func that, when called, runs canonical-ABI lower then invokes the named component func (= the host's import binding at `(module, name)` of `componentFuncIdx`) |
| canon-async builtins | produce core-funcs that route to the dispatcher |

The primary core module is the one whose canon-lift anchors
to the user's exports — `FindPrimaryCoreModuleIdx` already
traces this.

## Concrete flow for `filesystem-mkdir-rmdir`

```
core-module 0 = main
core-module 1 = wit-component:shim
core-module 2 = wit-component-fixup

core-instance 0   = Instantiate(module=1, [])           # shim
core-instance 1..19 = InstantiateCoreInline             # synthetic
                                                        # wasi:filesystem/preopens,
                                                        # wasi:filesystem/types,
                                                        # $root, … instances
                                                        # built from shim aliases +
                                                        # canon-lowered core-funcs
core-instance 20  = Instantiate(module=0, args=[        # MAIN
  ("wasi:filesystem/preopens@...", 1),
  ("wasi:filesystem/types@...",   2),
  ("$root",                       3),
  …
])
core-instance 21  = InstantiateCoreInline               # bundles
                                                        # host-bound "<slot>"
                                                        # functions + the table
core-instance 22  = Instantiate(module=2, args=[("",21)]) # FIXUP
                                                        # elem-segment fills
                                                        # shim's funcref table
```

Main is instantiated *before* fixup. That's fine — the
shim's `call_indirect` only fires at body-call time, not at
instantiate-time, so the table can be empty during main's
instantiate. The body is invoked after fixup populates.

## Implementation sketch

Add `MultiCoreInstantiator` in
`Wacs.ComponentModel/Runtime/`. Replace
`InstantiateMultiCore`'s body with:

```csharp
var inst = new MultiCoreInstantiator(component, coreBinaries, runtime);
inst.PreParse();          // parse all core modules
inst.RunHostImports(configureImports);  // configureImports → (mod, name) bindings
inst.WalkSections();      // file-order walk: core-instance, core-alias,
                          // canon, import, instance, alias sections
return inst.PrimaryInstance;
```

`WalkSections` maintains the parallel spaces and dispatches
to handlers. Each handler updates the spaces and (for
InstantiateCoreModule) re-binds aliased exports under target
import-module names before calling `runtime.InstantiateModule`.

For `canon-lower(componentFuncIdx)`, the resulting core-func
just *references* the existing host binding — no new binding
is created. The core-func space records the
`(module, methodName)` pair derived from the component's
import section by `componentFuncIdx`.

For `InstantiateCoreInline`, the result is a virtual bundle:
each export's value comes from the current core space at the
declared `(sort, idx)`.

For `InstantiateCoreModule.args`, walk each arg's source
instance (real or virtual). For each export, `BindHostFunction`
under `(arg.name, exportName)` with the same underlying
`IFunctionInstance` / `TableInstance` / etc. that the source
instance holds.

## Validation order

1. **cli-hello** must keep passing — it's a single
   `Instantiate(module=0, args=[...])` after the shim, no
   exercised funcref-table calls. The new path should behave
   identically for it.
2. **filesystem-mkdir-rmdir** is the target. Expect path-
   validation gaps in `Descriptor` to surface as the *next*
   layer of debugging (separate work).
3. All prior fixtures (wall-clock, random, monotonic, cli-env,
   cli-exit, cli-stdio, cli-stdio-roundtrip, cli-terminal) re-
   run as a regression suite.

## Scope estimate

~400-600 LOC over `MultiCoreInstantiator` + small parser /
runtime API additions (e.g. `BindExistingExternal((id), ExternalValue)`
to alias already-instantiated items under fresh names). Most
of the parser surface (`CoreInstanceEntry`, `ComponentAliasEntry`,
`CanonEntry`) already exists.
