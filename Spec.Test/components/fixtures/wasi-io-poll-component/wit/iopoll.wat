(module
  (import "wasi:io/poll@0.2.3" "poll"
    (func $poll (param i32 i32 i32)))
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
  (func (export "ask-poll") (param i32 i32) (result i32)
    (local $in i32) (local $r i32)
    ;; Allocate space for input list (2 i32 handles = 8 bytes).
    (local.set $in (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (i32.store (local.get $in) (local.get 0))
    (i32.store offset=4 (local.get $in) (local.get 1))
    ;; Allocate retArea for list<u32> = (ptr, len) = 8 bytes.
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $poll (local.get $in) (i32.const 2) (local.get $r))
    ;; Return the output list-len at retArea+4.
    (i32.load offset=4 (local.get $r))))
