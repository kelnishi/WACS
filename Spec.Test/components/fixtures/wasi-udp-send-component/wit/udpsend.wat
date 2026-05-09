(module
  (import "wasi:sockets/udp@0.2.8" "[method]outgoing-datagram-stream.send"
    (func $snd (param i32 i32 i32 i32)))
  (memory (export "memory") 1)
  (data (i32.const 100) "ping")
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
  (func (export "ask-send") (param i32) (result i64)
    (local $list i32) (local $r i32)
    ;; Allocate 1 element of outgoing-datagram = 44 bytes.
    (local.set $list (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 44)))
    ;; data: list<u8> at list+0/+4 — pointing to "ping"@100, len=4.
    (i32.store          (local.get $list) (i32.const 100))
    (i32.store offset=4 (local.get $list) (i32.const 4))
    ;; option<ip-socket-address> at list+8:
    ;;   list+8: option disc = 1 (Some)
    (i32.store8 offset=8 (local.get $list) (i32.const 1))
    ;;   list+12: variant disc = 0 (ipv4)
    (i32.store8 offset=12 (local.get $list) (i32.const 0))
    ;;   list+16: port = 7
    (i32.store16 offset=16 (local.get $list) (i32.const 7))
    ;;   list+18..21: 127.0.0.1
    (i32.store8 offset=18 (local.get $list) (i32.const 127))
    (i32.store8 offset=19 (local.get $list) (i32.const 0))
    (i32.store8 offset=20 (local.get $list) (i32.const 0))
    (i32.store8 offset=21 (local.get $list) (i32.const 1))
    ;; result<u64, _>: 16 bytes (1 disc + 7 pad + 8 u64)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 16)))
    (call $snd (local.get 0) (local.get $list) (i32.const 1) (local.get $r))
    (i64.load offset=8 (local.get $r))))
