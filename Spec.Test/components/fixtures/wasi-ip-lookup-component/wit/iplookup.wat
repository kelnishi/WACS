(module
  (import "wasi:sockets/ip-name-lookup@0.2.3" "resolve-addresses"
    (func $ra (param i32 i32 i32 i32)))
  (import "wasi:sockets/ip-name-lookup@0.2.3"
    "[method]resolve-address-stream.resolve-next-address"
    (func $rna (param i32 i32)))
  (import "wasi:sockets/ip-name-lookup@0.2.3"
    "[resource-drop]resolve-address-stream"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (data (i32.const 100) "example.com")
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
  (func (export "ask-lookup") (param i32) (result i32)
    (local $r1 i32) (local $r2 i32) (local $stream i32)
    (local $opt i32) (local $var i32)
    (local $a i32) (local $b i32) (local $c i32) (local $d i32)
    ;; result<own<stream>, error-code>: 8 bytes (disc + 3 pad + 4 handle)
    (local.set $r1 (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $ra (local.get 0) (i32.const 100) (i32.const 11) (local.get $r1))
    (local.set $stream (i32.load offset=4 (local.get $r1)))
    ;; result<option<ip-address>, error-code>: align 2, total 22 bytes.
    (local.set $r2 (call $realloc (i32.const 0) (i32.const 0) (i32.const 2) (i32.const 22)))
    (call $rna (local.get $stream) (local.get $r2))
    (local.set $opt (i32.load8_u offset=2 (local.get $r2)))
    (local.set $var (i32.load8_u offset=4 (local.get $r2)))
    (local.set $a (i32.load8_u offset=6 (local.get $r2)))
    (local.set $b (i32.load8_u offset=7 (local.get $r2)))
    (local.set $c (i32.load8_u offset=8 (local.get $r2)))
    (local.set $d (i32.load8_u offset=9 (local.get $r2)))
    (call $drop (local.get $stream))
    ;; Pack: stream-nonzero, option disc, variant disc, 4 address bytes
    (i32.or (i32.or (i32.or
      (i32.ne (local.get $stream) (i32.const 0))
      (i32.shl (local.get $opt) (i32.const 1)))
      (i32.shl (local.get $var) (i32.const 2)))
      (i32.or (i32.or
        (i32.shl (local.get $a) (i32.const 8))
        (i32.shl (local.get $b) (i32.const 16)))
        (i32.shl (local.get $c) (i32.const 24))))))
