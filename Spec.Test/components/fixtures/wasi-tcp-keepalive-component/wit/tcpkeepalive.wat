(module
  (import "wasi:sockets/tcp@0.2.3" "[method]tcp-socket.set-keep-alive-idle-time"
    (func $skait (param i32 i64 i32)))
  (import "wasi:sockets/tcp@0.2.3" "[method]tcp-socket.set-keep-alive-count"
    (func $skac (param i32 i32 i32)))
  (import "wasi:sockets/tcp@0.2.3" "[method]tcp-socket.keep-alive-count"
    (func $kac (param i32 i32)))
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
  (func (export "ask-keepalive") (param i32) (result i32)
    (local $r i32)
    ;; result<u64, _>: align 8, total 16
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 16)))
    (call $skait (local.get 0) (i64.const 1000) (local.get $r))
    ;; result<_, _>: align 1, total 2 — reuse the same retArea
    (call $skac (local.get 0) (i32.const 5) (local.get $r))
    ;; result<u32, _>: align 4, total 8
    (call $kac (local.get 0) (local.get $r))
    (i32.load offset=4 (local.get $r))))
