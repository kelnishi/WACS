(module
  ;; Imported canon-lowered wall-clock.now — takes
  ;; retAreaPtr, writes 12 bytes (u64 seconds + u32 nanoseconds).
  (import "wasi:clocks/wall-clock@0.2.3" "now"
    (func $now (param i32)))
  (memory (export "memory") 1)
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
  ;; wall-now() -> tuple<u64, u32>
  ;; Allocate 16-byte retArea (max align 8, total 12 bytes
  ;; padded to 16), call import to fill bytes 0..11, return
  ;; the ret-area pointer for canon-lift to read from.
  (func (export "wall-now") (result i32)
    (local $retArea i32)
    (local.set $retArea
      (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 16)))
    (call $now (local.get $retArea))
    (local.get $retArea)))
