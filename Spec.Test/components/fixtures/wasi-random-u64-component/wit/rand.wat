(module
  (import "wasi:random/random@0.2.3" "get-random-u64" (func $get_random_u64 (result i64)))
  (memory (export "memory") 1)
  (func (export "pick") (result i64)
    call $get_random_u64))
