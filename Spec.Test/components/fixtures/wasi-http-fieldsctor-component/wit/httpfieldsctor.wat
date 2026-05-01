(module
  (import "wasi:http/types@0.2.3" "[constructor]fields"
    (func $new (result i32)))
  (memory (export "memory") 1)
  (func (export "ask-new") (result i32)
    (call $new)))
