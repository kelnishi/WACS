(module
  (memory (export "memory") 1)
  ;; disc=2 (the third case "found"), payload at offset 4 = 42.
  (data (i32.const 100) "\02\00\00\00\2A\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 200)
  (export "cabi_realloc" (func $realloc))
  (func (export "lookup") (result i32) i32.const 100))
