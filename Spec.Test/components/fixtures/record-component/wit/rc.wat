(module
  (memory (export "memory") 1)
  (data (i32.const 100) "\07\00\00\00\0B\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 200)
  (export "cabi_realloc" (func $realloc))
  (func (export "origin") (result i32) i32.const 100))
