(module
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.accept"
    (func $acc (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.finish-connect"
    (func $fc (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[resource-drop]tcp-socket"
    (func $drop_sock (param i32)))
  (import "wasi:io/streams@0.2.8" "[resource-drop]input-stream"
    (func $drop_in (param i32)))
  (import "wasi:io/streams@0.2.8" "[resource-drop]output-stream"
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
  (func (export "ask-accept") (param i32) (result i32)
    (local $r i32) (local $a i32) (local $b i32) (local $c i32) (local $count i32)
    ;; result<tuple<own<sock>, own<in>, own<out>>, _>:
    ;; 1 disc + 3 pad + 12 bytes (3 handles) = 16 bytes
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $acc (local.get 0) (local.get $r))
    (local.set $a (i32.load offset=4 (local.get $r)))
    (local.set $b (i32.load offset=8 (local.get $r)))
    (local.set $c (i32.load offset=12 (local.get $r)))
    (local.set $count
      (i32.add (i32.add
        (i32.ne (local.get $a) (i32.const 0))
        (i32.ne (local.get $b) (i32.const 0)))
        (i32.ne (local.get $c) (i32.const 0))))
    (call $drop_sock (local.get $a))
    (call $drop_in (local.get $b))
    (call $drop_out (local.get $c))
    (local.get $count))
  (func (export "ask-connect") (param i32) (result i32)
    (local $r i32) (local $a i32) (local $b i32) (local $count i32)
    ;; result<tuple<own<in>, own<out>>, _>:
    ;; 1 disc + 3 pad + 8 bytes = 12 bytes
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $fc (local.get 0) (local.get $r))
    (local.set $a (i32.load offset=4 (local.get $r)))
    (local.set $b (i32.load offset=8 (local.get $r)))
    (local.set $count
      (i32.add
        (i32.ne (local.get $a) (i32.const 0))
        (i32.ne (local.get $b) (i32.const 0))))
    (call $drop_in (local.get $a))
    (call $drop_out (local.get $b))
    (local.get $count)))
