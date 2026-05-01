(module
  (import "wasi:filesystem/types@0.2.3" "[method]descriptor.is-same-object"
    (func $same (param i32 i32) (result i32)))
  (memory (export "memory") 1)
  (func (export "ask-same") (param i32 i32) (result i32)
    (call $same (local.get 0) (local.get 1))))
