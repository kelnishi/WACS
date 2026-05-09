(module
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.read-via-stream"
    (func $rvs (param i32 i64 i32)))
  (import "wasi:io/streams@0.2.8" "[method]input-stream.read"
    (func $read (param i32 i64 i32)))
  (import "wasi:io/streams@0.2.8" "[resource-drop]input-stream"
    (func $drop_is (param i32)))
  (import "wasi:filesystem/types@0.2.8" "[resource-drop]descriptor"
    (func $drop_d (param i32)))
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
  (func (export "read-first") (param i32 i64) (result i32)
    (local $r1 i32)
    (local $r2 i32)
    (local $is_handle i32)
    (local $count i32)
    ;; r1: retArea for read-via-stream (8 bytes: disc + handle).
    (local.set $r1 (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $rvs (local.get 0) (i64.const 0) (local.get $r1))
    (local.set $is_handle (i32.load offset=4 (local.get $r1)))
    ;; r2: retArea for read (12 bytes: disc + (ptr, count)).
    (local.set $r2 (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $read (local.get $is_handle) (local.get 1) (local.get $r2))
    (local.set $count (i32.load offset=8 (local.get $r2)))
    ;; Drop both resources.
    (call $drop_is (local.get $is_handle))
    (local.get $count)))
