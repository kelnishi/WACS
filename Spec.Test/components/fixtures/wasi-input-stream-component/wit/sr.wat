(module
  (import "wasi:io/streams@0.2.8" "[method]input-stream.read"
    (func $read (param i32 i64 i32)))
  (import "wasi:io/streams@0.2.8" "[resource-drop]input-stream"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  ;; try-read(handle: u32, len: u64) -> u32
  ;; Calls read(handle, len) into retArea, returns the
  ;; resulting list count from retArea+8.
  (func (export "try-read") (param i32 i64) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $read (local.get 0) (local.get 1) (local.get $r))
    (call $drop (local.get 0))
    ;; Return the bytes count at retArea+8.
    (i32.load offset=8 (local.get $r))))
