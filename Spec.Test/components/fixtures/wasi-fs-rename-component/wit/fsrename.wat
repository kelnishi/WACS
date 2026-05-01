(module
  (import "wasi:filesystem/types@0.2.3" "[method]descriptor.rename-at"
    (func $rename (param i32 i32 i32 i32 i32 i32 i32)))
  (memory (export "memory") 1)
  ;; "src" at 100 (3 bytes), "dst" at 200 (3 bytes).
  (data (i32.const 100) "src")
  (data (i32.const 200) "dst")
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
  (func (export "ask-rename") (param i32 i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    (call $rename (local.get 0)
                   (i32.const 100) (i32.const 3)
                   (local.get 1)
                   (i32.const 200) (i32.const 3)
                   (local.get $r))
    (i32.load8_u (local.get $r))))
