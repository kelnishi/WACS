(module
  (import "wasi:http/types@0.2.3" "[method]fields.delete"
    (func $del (param i32 i32 i32 i32)))
  (import "wasi:http/types@0.2.3" "[method]fields.clone"
    (func $clone (param i32) (result i32)))
  (import "wasi:http/types@0.2.3" "[resource-drop]fields"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (data (i32.const 100) "X-Custom")
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
  (func (export "ask-delete") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    (call $del (local.get 0) (i32.const 100) (i32.const 8) (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-clone") (param i32) (result i32)
    (local $h i32)
    (local.set $h (call $clone (local.get 0)))
    (call $drop (local.get $h))
    (i32.ne (local.get $h) (i32.const 0))))
