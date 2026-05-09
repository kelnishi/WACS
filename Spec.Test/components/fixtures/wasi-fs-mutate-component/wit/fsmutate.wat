(module
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.create-directory-at"
    (func $create (param i32 i32 i32 i32)))
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.remove-directory-at"
    (func $remove (param i32 i32 i32 i32)))
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.unlink-file-at"
    (func $unlink (param i32 i32 i32 i32)))
  (memory (export "memory") 1)
  ;; "child" path string at 100, 5 bytes.
  (data (i32.const 100) "child")
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
  (func (export "ask-mutate") (param i32) (result i32)
    (local $r i32) (local $sum i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    (call $create (local.get 0) (i32.const 100) (i32.const 5) (local.get $r))
    (local.set $sum (i32.load8_u (local.get $r)))
    (call $remove (local.get 0) (i32.const 100) (i32.const 5) (local.get $r))
    (local.set $sum (i32.add (local.get $sum) (i32.load8_u (local.get $r))))
    (call $unlink (local.get 0) (i32.const 100) (i32.const 5) (local.get $r))
    (local.set $sum (i32.add (local.get $sum) (i32.load8_u (local.get $r))))
    (local.get $sum)))
