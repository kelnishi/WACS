(module
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  ;; run() -> result<_, _>: returns 0 (Ok).
  (func (export "run") (result i32) i32.const 0))
