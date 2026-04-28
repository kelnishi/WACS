(module
  (import "wasi:http/types@0.2.3" "[method]request-options.set-connect-timeout"
    (func $set (param i32 i32 i64) (result i32)))
  (memory (export "memory") 1)
  (global $next (mut i32) (i32.const 1024))
  (func $realloc (param i32 i32 i32 i32) (result i32)
    (local $r i32) (local $align i32)
    (local.set $align (local.get 2))
    (global.set $next
      (i32.and
        (i32.add (global.get $next) (i32.sub (local.get $align) (i32.const 1)))
        (i32.xor (i32.const -1) (i32.sub (local.get $align) (i32.const 1)))))
    (local.set $r (global.get $next))
    (global.set $next
      (i32.add (global.get $next) (local.get 3)))
    (local.get $r))
  (export "cabi_realloc" (func $realloc))
  ;; set-connect-timeout returns result<_, _> = flat i32 disc (0=Ok).
  (func (export "ask-set-none") (param i32) (result i32)
    (call $set (local.get 0) (i32.const 0) (i64.const 0)))
  (func (export "ask-set-some") (param i32 i64) (result i32)
    (call $set (local.get 0) (i32.const 1) (local.get 1))))
