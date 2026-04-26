(module
  ;; Imported canon-lowered get-random-bytes — takes
  ;; (len: i64, retAreaPtr: i32), writes (dataPtr, count) at
  ;; retAreaPtr.
  (import "wasi:random/random@0.2.3" "get-random-bytes"
    (func $get_random_bytes (param i64 i32)))
  (memory (export "memory") 1)
  ;; Bump allocator at 1024 onwards. Same shape used by the
  ;; utf16-string-param-component fixture.
  (global $next (mut i32) (i32.const 1024))
  (func $realloc (param i32 i32 i32 i32) (result i32)
    (local $r i32)
    (local $align i32)
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
  ;; fetch(n: u64) -> list<u8>
  ;; Allocate retArea (8 bytes) on guest stack, call import,
  ;; return the retArea pointer (lifted by canon lift).
  (func (export "fetch") (param i64) (result i32)
    (local $retArea i32)
    (local.set $retArea
      (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $get_random_bytes (local.get 0) (local.get $retArea))
    (local.get $retArea)))
