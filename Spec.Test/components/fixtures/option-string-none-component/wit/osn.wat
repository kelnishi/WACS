(module
  (memory (export "memory") 1)
  ;; Return area at 200: disc=0 (None), payload bytes untouched.
  (data (i32.const 200) "\00\00\00\00\00\00\00\00\00\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "greeting") (result i32) i32.const 200))
