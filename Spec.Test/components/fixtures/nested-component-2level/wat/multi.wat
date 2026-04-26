(component
  ;; Innermost: defines `inner-greet` returning "Hi!".
  ;; Middle: composes innermost, alias-re-exports as `mid-greet`.
  ;; Outer: composes middle, alias-re-exports as `greet`.
  (component (;0;)
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
    (instance (;0;) (instantiate 0))
    (alias export 0 "inner-greet" (func (;0;)))
    (export (;1;) "mid-greet" (func 0)))

  (instance (;0;) (instantiate 0))
  (alias export 0 "mid-greet" (func (;0;)))
  (export (;1;) "greet" (func 0))
)
