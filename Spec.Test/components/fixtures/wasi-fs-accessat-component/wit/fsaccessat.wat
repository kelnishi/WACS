(module
  (import "wasi:filesystem/types@0.2.3" "[method]descriptor.access-at"
    (func $aa (param i32 i32 i32 i32 i32 i32 i32)))
  (memory (export "memory") 1)
  (data (i32.const 100) "f")
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
  ;; Wire: self, path-flags, path-ptr, path-len, type-disc,
  ;;       type-modes (don't-care for exists), retArea
  (func (export "ask-access-readable") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    ;; access(readable | writable = 3)
    (call $aa (local.get 0) (i32.const 0)
              (i32.const 100) (i32.const 1)
              (i32.const 0)        ;; access-type disc = access
              (i32.const 3)        ;; modes = readable | writable
              (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-access-exists") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    (call $aa (local.get 0) (i32.const 0)
              (i32.const 100) (i32.const 1)
              (i32.const 1)        ;; access-type disc = exists
              (i32.const 0)        ;; modes ignored
              (local.get $r))
    (i32.load8_u (local.get $r))))
