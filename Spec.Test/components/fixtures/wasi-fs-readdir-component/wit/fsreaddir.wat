(module
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.read-directory"
    (func $rd (param i32 i32)))
  (import "wasi:filesystem/types@0.2.8" "[method]directory-entry-stream.read-directory-entry"
    (func $rde (param i32 i32)))
  (import "wasi:filesystem/types@0.2.8" "[resource-drop]directory-entry-stream"
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
  (func (export "ask-readdir") (param i32) (result i32)
    (local $r1 i32) (local $r2 i32) (local $stream i32)
    (local $opt i32) (local $type i32) (local $namePtr i32)
    (local $nameLen i32) (local $firstChar i32)
    ;; result<own<des>, _>: 8 bytes
    (local.set $r1 (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $rd (local.get 0) (local.get $r1))
    (local.set $stream (i32.load offset=4 (local.get $r1)))
    ;; result<option<directory-entry>, _>: 20 bytes, align 4
    (local.set $r2 (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 20)))
    (call $rde (local.get $stream) (local.get $r2))
    (local.set $opt (i32.load8_u offset=4 (local.get $r2)))
    (local.set $type (i32.load8_u offset=8 (local.get $r2)))
    (local.set $namePtr (i32.load offset=12 (local.get $r2)))
    (local.set $nameLen (i32.load offset=16 (local.get $r2)))
    (local.set $firstChar (i32.load8_u (local.get $namePtr)))
    (call $drop (local.get $stream))
    (i32.or (i32.or
      (local.get $opt)
      (i32.shl (local.get $type) (i32.const 8)))
      (i32.or
        (i32.shl (local.get $nameLen) (i32.const 16))
        (i32.shl (local.get $firstChar) (i32.const 24))))))
