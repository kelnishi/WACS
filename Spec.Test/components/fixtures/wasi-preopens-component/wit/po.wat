(module
  (import "wasi:filesystem/preopens@0.2.8" "get-directories"
    (func $get (param i32)))
  (import "wasi:filesystem/types@0.2.8" "[resource-drop]descriptor"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  ;; Bump allocator from 1024 onward — fresh address per call.
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
  (func (export "count") (result i32)
    (local $r i32)
    (local $arr i32)
    (local $cnt i32)
    (local $i i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $get (local.get $r))
    (local.set $arr (i32.load (local.get $r)))
    (local.set $cnt (i32.load offset=4 (local.get $r)))
    (local.set $i (i32.const 0))
    (block $end
      (loop $loop
        (br_if $end (i32.ge_u (local.get $i) (local.get $cnt)))
        (call $drop (i32.load (i32.add (local.get $arr) (i32.mul (local.get $i) (i32.const 12)))))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $loop)))
    (local.get $cnt)))
