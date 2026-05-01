(module
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.scheme"
    (func $getSch (param i32 i32)))
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
  ;; option<scheme> retArea is 16 bytes:
  ;;   0:  option disc (u8)
  ;;   1-3: padding
  ;;   4:  variant disc (u8)
  ;;   5-7: padding
  ;;   8:  string ptr (i32)
  ;;   12: string len (i32)
  (func (export "ask-scheme-disc") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $getSch (local.get 0) (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-scheme-variant") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $getSch (local.get 0) (local.get $r))
    (i32.load8_u offset=4 (local.get $r)))
  (func (export "ask-scheme-other-len") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $getSch (local.get 0) (local.get $r))
    (i32.load offset=12 (local.get $r))))
