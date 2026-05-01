(module
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 100)
  (export "cabi_realloc" (func $realloc))
  ;; Returns 0b101 = read | execute (read=bit0, execute=bit2).
  (func (export "default-perms") (result i32) i32.const 5))
