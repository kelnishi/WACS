(module
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.path-with-query"
    (func $pq (param i32 i32)))
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.authority"
    (func $au (param i32 i32)))
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
  (func (export "ask-path") (param i32) (result i32)
    (local $r i32) (local $disc i32) (local $strPtr i32) (local $firstByte i32)
    ;; option<string>: 12 bytes, align 4
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $pq (local.get 0) (local.get $r))
    (local.set $disc (i32.load8_u (local.get $r)))
    (local.set $strPtr (i32.load offset=4 (local.get $r)))
    (local.set $firstByte
      (if (result i32) (i32.eqz (local.get $disc))
        (then (i32.const 0))
        (else (i32.load8_u (local.get $strPtr)))))
    (i32.or
      (local.get $disc)
      (i32.shl (local.get $firstByte) (i32.const 8))))
  (func (export "ask-authority") (param i32) (result i32)
    (local $r i32) (local $disc i32) (local $strPtr i32) (local $firstByte i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $au (local.get 0) (local.get $r))
    (local.set $disc (i32.load8_u (local.get $r)))
    (local.set $strPtr (i32.load offset=4 (local.get $r)))
    (local.set $firstByte
      (if (result i32) (i32.eqz (local.get $disc))
        (then (i32.const 0))
        (else (i32.load8_u (local.get $strPtr)))))
    (i32.or
      (local.get $disc)
      (i32.shl (local.get $firstByte) (i32.const 8)))))
