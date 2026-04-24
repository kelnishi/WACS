(module
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 100)
  (export "cabi_realloc" (func $realloc))
  ;; Returns enum case index 2 = West.
  (func (export "current") (result i32) i32.const 2))
