(module
  (import "wasi:io/streams@0.2.8" "[method]output-stream.write-zeroes"
    (func $wz (param i32 i64 i32)))
  (import "wasi:io/streams@0.2.8" "[method]output-stream.blocking-write-zeroes-and-flush"
    (func $bwzf (param i32 i64 i32)))
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
  (func (export "ask-zeroes") (param i32) (result i32)
    (local $r i32) (local $sum i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 4)))
    (call $wz (local.get 0) (i64.const 8) (local.get $r))
    (local.set $sum (i32.load8_u (local.get $r)))
    (call $bwzf (local.get 0) (i64.const 16) (local.get $r))
    (local.set $sum (i32.add (local.get $sum) (i32.load8_u (local.get $r))))
    (local.get $sum)))
