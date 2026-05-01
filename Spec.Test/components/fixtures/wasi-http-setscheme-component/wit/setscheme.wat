(module
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.set-scheme"
    (func $setSch (param i32 i32 i32 i32 i32) (result i32)))
  (memory (export "memory") 1)
  (data (i32.const 256) "ftp")
  (func (export "ask-set-none") (param i32) (result i32)
    ;; set-scheme(req, None): optDisc=0, all rest 0
    (call $setSch (local.get 0) (i32.const 0) (i32.const 0) (i32.const 0) (i32.const 0)))
  (func (export "ask-set-https") (param i32) (result i32)
    ;; set-scheme(req, Some(HTTPS)): optDisc=1, varDisc=1, ptr=0, len=0
    (call $setSch (local.get 0) (i32.const 1) (i32.const 1) (i32.const 0) (i32.const 0)))
  (func (export "ask-set-other") (param i32) (result i32)
    ;; set-scheme(req, Some(Other("ftp"))): optDisc=1, varDisc=2, ptr=256, len=3
    (call $setSch (local.get 0) (i32.const 1) (i32.const 2) (i32.const 256) (i32.const 3))))
