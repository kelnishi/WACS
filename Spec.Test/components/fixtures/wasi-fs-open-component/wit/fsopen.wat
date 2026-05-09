(module
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.open-at"
    (func $open (param i32 i32 i32 i32 i32 i32 i32)))
  (import "wasi:filesystem/types@0.2.8" "[resource-drop]descriptor"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  ;; "child" path string at 100, 5 bytes.
  (data (i32.const 100) "child")
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
  (func (export "ask-open") (param i32) (result i32)
    (local $r i32) (local $h i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    ;; open-at(self, path-flags=0, path="child"@100/5, open-flags=Create=1, descriptor-flags=0, retArea=$r)
    (call $open (local.get 0) (i32.const 0) (i32.const 100) (i32.const 5)
                 (i32.const 1) (i32.const 0) (local.get $r))
    ;; Ok payload (i32 handle) at retArea+4.
    (local.set $h (i32.load offset=4 (local.get $r)))
    ;; Drop the returned handle so the test's resource-table count check is clean.
    (call $drop (local.get $h))
    (local.get $h)))
