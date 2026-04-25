(module
  (memory (export "memory") 1)
  ;; retArea at 200: disc=1 ("dot" case), padding to 4-byte
  ;; alignment of point (max field align is 4), then x=7, y=11.
  (data (i32.const 200) "\01\00\00\00\07\00\00\00\0B\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "locate") (result i32) i32.const 200))
