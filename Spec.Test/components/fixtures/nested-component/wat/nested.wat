(component
  ;; Nested component: defines and exports `inner-greet`.
  (component (;0;)
    (core module (;0;)
      (memory (export "memory") 1)
      (data (i32.const 100) "Hi!")
      (data (i32.const 200) "\64\00\00\00\03\00\00\00")
      (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 300)
      (export "cabi_realloc" (func $realloc))
      (func (export "inner-greet") (result i32) i32.const 200))
    (core instance (;0;) (instantiate 0))
    (alias core export 0 "memory" (core memory (;0;)))
    (alias core export 0 "cabi_realloc" (core func (;0;)))
    (alias core export 0 "inner-greet" (core func (;1;)))
    (type (;0;) (func (result string)))
    (func (;0;) (type 0) (canon lift (core func 1) (memory 0) (realloc 0)))
    (export (;1;) "inner-greet" (func 0)))

  ;; Outer component instantiates the nested one and re-exports.
  (instance (;0;) (instantiate 0))
  (alias export 0 "inner-greet" (func (;0;)))
  (export (;1;) "greet" (func 0))
)
