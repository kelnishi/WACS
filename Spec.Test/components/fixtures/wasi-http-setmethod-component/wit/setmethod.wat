(module
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.set-method"
    (func $setMeth (param i32 i32 i32 i32) (result i32)))
  (memory (export "memory") 1)
  (data (i32.const 256) "PURGE")
  (func (export "ask-set-get") (param i32) (result i32)
    ;; set-method(req, Get): disc=0, ptr=0, len=0
    (call $setMeth (local.get 0) (i32.const 0) (i32.const 0) (i32.const 0)))
  (func (export "ask-set-post") (param i32) (result i32)
    ;; set-method(req, Post): disc=2, ptr=0, len=0
    (call $setMeth (local.get 0) (i32.const 2) (i32.const 0) (i32.const 0)))
  (func (export "ask-set-other") (param i32) (result i32)
    ;; set-method(req, Other("PURGE")): disc=9, ptr=256, len=5
    (call $setMeth (local.get 0) (i32.const 9) (i32.const 256) (i32.const 5))))
