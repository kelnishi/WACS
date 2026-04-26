(module
  (import "wasi:clocks/timezone@0.2.3" "utc-offset"
    (func $offset (param i64 i32) (result i32)))
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  (func (export "ask-offset") (param i64 i32) (result i32)
    (call $offset (local.get 0) (local.get 1))))
