(module
  (import "wasi:io/streams@0.2.3" "[method]output-stream.write"
    (func $write (param i32 i32 i32 i32)))
  (import "wasi:io/streams@0.2.3" "[resource-drop]output-stream"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (data (i32.const 200) "hello")
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  ;; try-write(handle: u32) -> u32 — calls write(handle, "hello"), drops, returns 0.
  (func (export "try-write") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $write (local.get 0) (i32.const 200) (i32.const 5) (local.get $r))
    (call $drop (local.get 0))
    (i32.const 0)))
