(module
  (memory (export "memory") 1)
  (data (i32.const 200) "\07\00\00\00\23\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "pair") (result i32)
    i32.const 200))
