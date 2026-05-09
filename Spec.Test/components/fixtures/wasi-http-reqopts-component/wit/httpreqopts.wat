(module
  (import "wasi:http/types@0.2.8" "[method]request-options.connect-timeout"
    (func $ct (param i32 i32)))
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
  ;; option<u64>: align 8, total 16 bytes (disc + 7 pad + u64)
  (func (export "ask-disc") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 16)))
    (call $ct (local.get 0) (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-timeout") (param i32) (result i64)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 16)))
    (call $ct (local.get 0) (local.get $r))
    (i64.load offset=8 (local.get $r))))
