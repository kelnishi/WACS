(module
  (import "wasi:http/types@0.2.3" "[constructor]request-options"
    (func $newOpts (result i32)))
  (import "wasi:http/types@0.2.3" "[constructor]outgoing-request"
    (func $newReq (param i32) (result i32)))
  (import "wasi:http/types@0.2.3" "[constructor]outgoing-response"
    (func $newResp (param i32) (result i32)))
  (memory (export "memory") 1)
  (func (export "ask-new-request-options") (result i32)
    (call $newOpts))
  (func (export "ask-new-outgoing-request") (param i32) (result i32)
    (call $newReq (local.get 0)))
  (func (export "ask-new-outgoing-response") (param i32) (result i32)
    (call $newResp (local.get 0))))
