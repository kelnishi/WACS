(module
  (memory (export "memory") 1)
  (data (i32.const 100) "denied")
  ;; retArea at 200: disc=1 (the second case "denied"), padding,
  ;; payload (strPtr=100, strLen=6) at offset 4.
  (data (i32.const 200) "\01\00\00\00\64\00\00\00\06\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "describe") (result i32) i32.const 200))
