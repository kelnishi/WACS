(module
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.finish-bind"
    (func $fb (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.start-listen"
    (func $sl (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.finish-listen"
    (func $fl (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.shutdown"
    (func $sh (param i32 i32 i32)))
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
  (func (export "ask-listen") (param i32) (result i32)
    (local $r i32) (local $sum i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    (call $fb (local.get 0) (local.get $r))
    (local.set $sum (i32.load8_u (local.get $r)))
    (call $sl (local.get 0) (local.get $r))
    (local.set $sum (i32.add (local.get $sum) (i32.load8_u (local.get $r))))
    (call $fl (local.get 0) (local.get $r))
    (local.set $sum (i32.add (local.get $sum) (i32.load8_u (local.get $r))))
    ;; shutdown(both=2)
    (call $sh (local.get 0) (i32.const 2) (local.get $r))
    (local.set $sum (i32.add (local.get $sum) (i32.load8_u (local.get $r))))
    (local.get $sum)))
