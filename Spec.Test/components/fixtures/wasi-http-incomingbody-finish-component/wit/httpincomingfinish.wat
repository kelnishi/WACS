(module
  (import "wasi:http/types@0.2.8" "[static]incoming-body.finish"
    (func $finish (param i32) (result i32)))
  (memory (export "memory") 1)
  (func (export "ask-finish") (param i32) (result i32)
    (call $finish (local.get 0))))
