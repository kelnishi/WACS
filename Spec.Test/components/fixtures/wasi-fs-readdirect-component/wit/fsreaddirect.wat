(module
  (import "wasi:filesystem/types@0.2.3" "[method]descriptor.read"
    (func $rd (param i32 i64 i64 i32)))
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
  (func (export "ask-read") (param i32) (result i32)
    (local $r i32) (local $len i32) (local $eof i32)
    ;; Result wrapper around tuple<list<u8>, bool> = 16 bytes,
    ;; align 4: disc(1) + pad(3) + list-ptr(4) + list-len(4) +
    ;; eof(1) + pad(3).
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $rd (local.get 0) (i64.const 8) (i64.const 0) (local.get $r))
    ;; Read list-len at retArea+8.
    (local.set $len (i32.load offset=8 (local.get $r)))
    ;; Read eof at retArea+12.
    (local.set $eof (i32.load8_u offset=12 (local.get $r)))
    (i32.or
      (i32.shl (local.get $len) (i32.const 1))
      (local.get $eof))))
