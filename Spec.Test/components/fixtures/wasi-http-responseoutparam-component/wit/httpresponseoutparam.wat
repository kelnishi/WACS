(module
  (import "wasi:http/types@0.2.3" "[static]response-outparam.set"
    (func $set (param i32 i32 i32)))
  (memory (export "memory") 1)
  (func (export "ask-set-ok") (param i32 i32) (result i32)
    ;; set(param, Ok(response)) — outer disc=0, payload=resp handle
    (call $set (local.get 0) (i32.const 0) (local.get 1))
    (i32.const 0))
  (func (export "ask-set-err") (param i32) (result i32)
    ;; set(param, Err(internal-error)) — outer disc=1, payload=0
    ;; (single-case error-code; the joined-flat slot value is ignored)
    (call $set (local.get 0) (i32.const 1) (i32.const 0))
    (i32.const 0)))
