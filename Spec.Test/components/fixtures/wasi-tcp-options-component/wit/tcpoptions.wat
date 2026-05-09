(module
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.is-listening"
    (func $isl (param i32) (result i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.set-hop-limit"
    (func $shl (param i32 i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.hop-limit"
    (func $hl (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.set-keep-alive-enabled"
    (func $skae (param i32 i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.keep-alive-enabled"
    (func $kae (param i32 i32)))
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
  (func (export "ask-options") (param i32) (result i32)
    (local $r i32) (local $listening i32) (local $hop i32) (local $kae i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    ;; is-listening — bare bool return.
    (local.set $listening (call $isl (local.get 0)))
    ;; set-hop-limit(42)
    (call $shl (local.get 0) (i32.const 42) (local.get $r))
    ;; hop-limit() — read at retArea+1
    (call $hl (local.get 0) (local.get $r))
    (local.set $hop (i32.load8_u offset=1 (local.get $r)))
    ;; set-keep-alive-enabled(true=1)
    (call $skae (local.get 0) (i32.const 1) (local.get $r))
    ;; keep-alive-enabled() — read bool at retArea+1
    (call $kae (local.get 0) (local.get $r))
    (local.set $kae (i32.load8_u offset=1 (local.get $r)))
    ;; Pack: (kae << 16) | (hop << 8) | listening — caller
    ;; checks each byte independently.
    (i32.or (i32.or
      (i32.shl (local.get $kae) (i32.const 16))
      (i32.shl (local.get $hop) (i32.const 8)))
      (local.get $listening))))
