(module
  (import "wasi:sockets/udp@0.2.3" "[method]udp-socket.stream"
    (func $st (param i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32)))
  (import "wasi:sockets/udp@0.2.3" "[resource-drop]incoming-datagram-stream"
    (func $drop_in (param i32)))
  (import "wasi:sockets/udp@0.2.3" "[resource-drop]outgoing-datagram-stream"
    (func $drop_out (param i32)))
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
  (func (export "ask-stream-none") (param i32) (result i32)
    (local $r i32) (local $a i32) (local $b i32) (local $count i32)
    ;; result<tuple<own<r1>, own<r2>>, _>: 12 bytes (1 disc + 3 pad + 8 handles)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    ;; option-disc=0 (None); rest are don't-care padding.
    (call $st (local.get 0)
              (i32.const 0)        ;; option disc = None
              (i32.const 0) (i32.const 0) (i32.const 0)
              (i32.const 0) (i32.const 0) (i32.const 0)
              (i32.const 0) (i32.const 0) (i32.const 0)
              (i32.const 0) (i32.const 0) (i32.const 0)
              (local.get $r))
    (local.set $a (i32.load offset=4 (local.get $r)))
    (local.set $b (i32.load offset=8 (local.get $r)))
    (local.set $count (i32.add
      (i32.ne (local.get $a) (i32.const 0))
      (i32.ne (local.get $b) (i32.const 0))))
    (call $drop_in (local.get $a))
    (call $drop_out (local.get $b))
    (local.get $count))
  (func (export "ask-stream-some") (param i32) (result i32)
    (local $r i32) (local $a i32) (local $b i32) (local $count i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    ;; option Some + ipv4(127.0.0.1:5000)
    (call $st (local.get 0)
              (i32.const 1)        ;; option disc = Some
              (i32.const 0)        ;; variant disc = ipv4
              (i32.const 5000)     ;; port
              (i32.const 127)      ;; addr[0]
              (i32.const 0)        ;; addr[1]
              (i32.const 0)        ;; addr[2]
              (i32.const 1)        ;; addr[3]
              (i32.const 0) (i32.const 0) (i32.const 0)
              (i32.const 0) (i32.const 0) (i32.const 0)
              (local.get $r))
    (local.set $a (i32.load offset=4 (local.get $r)))
    (local.set $b (i32.load offset=8 (local.get $r)))
    (local.set $count (i32.add
      (i32.ne (local.get $a) (i32.const 0))
      (i32.ne (local.get $b) (i32.const 0))))
    (call $drop_in (local.get $a))
    (call $drop_out (local.get $b))
    (local.get $count)))
