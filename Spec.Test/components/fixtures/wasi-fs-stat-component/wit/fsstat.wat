(module
  (import "wasi:filesystem/types@0.2.8" "[method]descriptor.stat"
    (func $stat (param i32 i32)))
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
  ;; result<descriptor-stat, _>: 104 bytes, align 8.
  ;; Layout (relative to retArea):
  ;;   +0:  outer disc
  ;;   +8:  type (u8)
  ;;   +16: link-count (u64)
  ;;   +24: size (u64)
  ;;   +32: data-access-timestamp option<datetime> (24 bytes)
  ;;     +32: option disc, +40: seconds, +48: nanos
  ;;   +56: data-mod-timestamp option<datetime>
  ;;     +56: option disc, +64: seconds, +72: nanos
  ;;   +80: status-change-timestamp option<datetime>
  (func (export "ask-stat-type") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 104)))
    (call $stat (local.get 0) (local.get $r))
    (i32.load8_u offset=8 (local.get $r)))
  (func (export "ask-stat-size") (param i32) (result i64)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 104)))
    (call $stat (local.get 0) (local.get $r))
    (i64.load offset=24 (local.get $r)))
  (func (export "ask-stat-mtime-disc") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 104)))
    (call $stat (local.get 0) (local.get $r))
    (i32.load8_u offset=56 (local.get $r)))
  (func (export "ask-stat-mtime") (param i32) (result i64)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 104)))
    (call $stat (local.get 0) (local.get $r))
    (i64.load offset=64 (local.get $r))))
