(module
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.set-path-with-query"
    (func $setPath (param i32 i32 i32 i32 i32)))
  (import "wasi:http/types@0.2.3" "[method]outgoing-request.set-authority"
    (func $setAuth (param i32 i32 i32 i32 i32)))
  (memory (export "memory") 1)
  ;; Pre-fill UTF-8 buffers at fixed offsets.
  ;; "/foo/bar?x=1" (12 bytes) at offset 256
  (data (i32.const 256) "/foo/bar?x=1")
  ;; "example.com:443" (15 bytes) at offset 384
  (data (i32.const 384) "example.com:443")
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
  (func (export "ask-set-path-none") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 4)))
    ;; set-path(req, None: disc=0, ptr=0, len=0)
    (call $setPath (local.get 0) (i32.const 0) (i32.const 0) (i32.const 0) (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-set-path-some") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 4)))
    ;; set-path(req, Some("/foo/bar?x=1"): disc=1, ptr=256, len=12)
    (call $setPath (local.get 0) (i32.const 1) (i32.const 256) (i32.const 12) (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-set-authority-some") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 4)))
    ;; set-authority(req, Some("example.com:443"): disc=1, ptr=384, len=15)
    (call $setAuth (local.get 0) (i32.const 1) (i32.const 384) (i32.const 15) (local.get $r))
    (i32.load8_u (local.get $r))))
