(module
  (import "wasi:http/types@0.2.3" "[method]incoming-response.status"
    (func $st (param i32) (result i32)))
  (import "wasi:http/types@0.2.3" "[method]incoming-response.headers"
    (func $hd (param i32) (result i32)))
  (import "wasi:http/types@0.2.3" "[method]outgoing-response.status-code"
    (func $osc (param i32) (result i32)))
  ;; set-status-code returns result<_, _> = flat i32 disc.
  (import "wasi:http/types@0.2.3" "[method]outgoing-response.set-status-code"
    (func $sosc (param i32 i32) (result i32)))
  (import "wasi:http/types@0.2.3" "[resource-drop]fields"
    (func $drop_fields (param i32)))
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
  (func (export "ask-status") (param i32) (result i32)
    (call $st (local.get 0)))
  (func (export "ask-set-status") (param i32) (result i32)
    (drop (call $sosc (local.get 0) (i32.const 404)))
    (call $osc (local.get 0)))
  (func (export "ask-headers") (param i32) (result i32)
    (local $h i32)
    (local.set $h (call $hd (local.get 0)))
    (call $drop_fields (local.get $h))
    (i32.ne (local.get $h) (i32.const 0))))
