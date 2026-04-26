(module
  (memory (export "memory") 1)
  ;; Bump allocator: each cabi_realloc call returns the next
  ;; chunk and bumps the cursor. align (param 2) is honored by
  ;; rounding the cursor up before allocating; size is param 3.
  (global $next (mut i32) (i32.const 1024))
  (func $realloc (param i32 i32 i32 i32) (result i32)
    (local $r i32)
    (local $align i32)
    (local.set $align (local.get 2))
    ;; Round $next up to alignment.
    (global.set $next
      (i32.and
        (i32.add (global.get $next) (i32.sub (local.get $align) (i32.const 1)))
        (i32.xor (i32.const -1) (i32.sub (local.get $align) (i32.const 1)))))
    (local.set $r (global.get $next))
    (global.set $next
      (i32.add (global.get $next) (local.get 3)))
    (local.get $r))
  (export "cabi_realloc" (func $realloc))
  ;; echo(strPtr: i32, codeUnits: i32) -> i32 retArea
  ;; — copies (strPtr, codeUnits) into a fresh retArea so the
  ;; lift sees the input unchanged. Round-trip test: caller
  ;; passes "Hello" → guest UTF-16 buffer at strPtr → echo
  ;; returns retArea where (strPtr, codeUnits) lives → lift
  ;; decodes back to "Hello".
  (func (export "echo") (param i32 i32) (result i32)
    (local $retArea i32)
    (local.set $retArea
      (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (i32.store (local.get $retArea) (local.get 0))
    (i32.store offset=4 (local.get $retArea) (local.get 1))
    (local.get $retArea)))
