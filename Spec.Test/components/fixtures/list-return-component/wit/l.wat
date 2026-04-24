(module
  (memory (export "memory") 1)
  (data (i32.const 100) "\01\02\03\04\05")
  (data (i32.const 200) "\64\00\00\00\05\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "bytes") (result i32)
    i32.const 200))
