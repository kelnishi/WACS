(module
  (import "wasi:sockets/tcp-create-socket@0.2.3" "create-tcp-socket"
    (func $create (param i32 i32)))
  (import "wasi:sockets/tcp@0.2.3" "[resource-drop]tcp-socket"
    (func $drop (param i32)))
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
  (func (export "try-create") (param i32) (result i32)
    (local $r i32)
    (local $disc i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $create (local.get 0) (local.get $r))
    (local.set $disc (i32.load8_u (local.get $r)))
    (if (i32.eqz (local.get $disc))
      (then (call $drop (i32.load offset=4 (local.get $r)))))
    (local.get $disc)))
