(module
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.readlink-at"
    (func $rl (param i32 i32 i32 i32)))
  (memory (export "memory") 1)
  (data (i32.const 100) "src")
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
  ;; ask-readlink: returns string. Allocates 8-byte export
  ;; retArea + 12-byte inner retArea (result<string, _>),
  ;; calls readlink-at, copies the (ptr, len) at inner+4/+8
  ;; to the export retArea, returns the export retArea ptr.
  (func (export "ask-readlink") (param i32) (result i32)
    (local $exp i32) (local $r i32)
    (local.set $exp (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $rl (local.get 0) (i32.const 100) (i32.const 3) (local.get $r))
    (i32.store          (local.get $exp) (i32.load offset=4 (local.get $r)))
    (i32.store offset=4 (local.get $exp) (i32.load offset=8 (local.get $r)))
    (local.get $exp)))
