(module
  ;; Canonical-ABI flat lowering — 9 wire slots:
  ;;   paramHandle, resultDisc,
  ;;   rp0 (i32), rp1 (i32), rp2 (i64), rp3..rp6 (i32).
  (import "wasi:http/types@0.2.3" "[static]response-outparam.set"
    (func $set (param i32 i32 i32 i32 i64 i32 i32 i32 i32)))
  (memory (export "memory") 1)
  ;; "bad" UTF-8 bytes at offset 256.
  (data (i32.const 256) "bad")
  (func (export "ask-set-ok") (param i32 i32) (result i32)
    ;; Ok(response): resultDisc=0, rp0=response handle, rest zero.
    (call $set (local.get 0) (i32.const 0) (local.get 1)
      (i32.const 0) (i64.const 0)
      (i32.const 0) (i32.const 0) (i32.const 0) (i32.const 0))
    (i32.const 0))
  (func (export "ask-set-err") (param i32) (result i32)
    ;; Err(internal-error(None)): resultDisc=1, errDisc=38 (rp0),
    ;; opt-disc=0 (rp1), rest zero.
    (call $set (local.get 0) (i32.const 1) (i32.const 38)
      (i32.const 0) (i64.const 0)
      (i32.const 0) (i32.const 0) (i32.const 0) (i32.const 0))
    (i32.const 0))
  (func (export "ask-set-err-bodysize") (param i32) (result i32)
    ;; Err(HTTP-request-body-size(Some(12345))): errDisc=17,
    ;; opt-disc=1 (rp1), val=12345 (rp2 i64). rp3..rp6=0.
    (call $set (local.get 0) (i32.const 1) (i32.const 17)
      (i32.const 1) (i64.const 12345)
      (i32.const 0) (i32.const 0) (i32.const 0) (i32.const 0))
    (i32.const 0))
  (func (export "ask-set-err-internal-msg") (param i32) (result i32)
    ;; Err(internal-error(Some("bad"))): errDisc=38, opt-disc=1
    ;; (rp1), ptr=256 (rp2 narrowed i32→i64), len=3 (rp3).
    (call $set (local.get 0) (i32.const 1) (i32.const 38)
      (i32.const 1) (i64.const 256)
      (i32.const 3) (i32.const 0) (i32.const 0) (i32.const 0))
    (i32.const 0)))
