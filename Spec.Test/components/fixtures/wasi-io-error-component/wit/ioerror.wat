(module
  (import "wasi:io/error@0.2.8" "[method]error.to-debug-string"
    (func $tds (param i32 i32)))
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
  ;; ask-debug: returns string. Callee-allocates an 8-byte
  ;; retArea, calls error.to-debug-string into it, and returns
  ;; the retArea pointer. Outer canon-lift reads (ptr, len)
  ;; at retArea / retArea+4.
  (func (export "ask-debug") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $tds (local.get 0) (local.get $r))
    (local.get $r)))
