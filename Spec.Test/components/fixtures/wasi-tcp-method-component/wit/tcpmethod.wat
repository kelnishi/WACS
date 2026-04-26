(module
  (import "wasi:sockets/tcp@0.2.3" "[method]tcp-socket.address-family"
    (func $family (param i32) (result i32)))
  (import "wasi:sockets/tcp@0.2.3" "[method]tcp-socket.subscribe"
    (func $subscribe (param i32) (result i32)))
  (import "wasi:sockets/tcp@0.2.3" "[resource-drop]tcp-socket"
    (func $drop_sock (param i32)))
  (import "wasi:io/poll@0.2.3" "[resource-drop]pollable"
    (func $drop_pol (param i32)))
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
  (func (export "ask-family") (param i32) (result i32)
    (local $fam i32) (local $pol i32)
    (local.set $fam (call $family (local.get 0)))
    (local.set $pol (call $subscribe (local.get 0)))
    (call $drop_pol (local.get $pol))
    (local.get $fam)))
