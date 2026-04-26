(module
  (import "wasi:sockets/tcp@0.2.3" "[method]tcp-socket.local-address"
    (func $la (param i32 i32)))
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
  ;; result<ip-socket-address, _>: align 4, total 36 bytes.
  ;; Layout:
  ;;   +0:  outer disc
  ;;   +4:  variant disc (0=ipv4, 1=ipv6)
  ;;   +8:  port (u16)
  ;;   +10: ipv4 4-byte address OR ipv6 padding+...
  (func (export "ask-local-disc") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 36)))
    (call $la (local.get 0) (local.get $r))
    (i32.load8_u offset=4 (local.get $r)))
  (func (export "ask-local-port") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 36)))
    (call $la (local.get 0) (local.get $r))
    ;; Read port (u16) at retArea+8.
    (i32.load16_u offset=8 (local.get $r)))
  (func (export "ask-local-ipv4") (param i32) (result i32)
    (local $r i32) (local $a i32) (local $b i32) (local $c i32) (local $d i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 36)))
    (call $la (local.get 0) (local.get $r))
    ;; ipv4 address bytes at retArea+10..+13
    (local.set $a (i32.load8_u offset=10 (local.get $r)))
    (local.set $b (i32.load8_u offset=11 (local.get $r)))
    (local.set $c (i32.load8_u offset=12 (local.get $r)))
    (local.set $d (i32.load8_u offset=13 (local.get $r)))
    (i32.or (i32.or
      (i32.shl (local.get $a) (i32.const 24))
      (i32.shl (local.get $b) (i32.const 16)))
      (i32.or
        (i32.shl (local.get $c) (i32.const 8))
        (local.get $d)))))
