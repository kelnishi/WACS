(module
  (import "wasi:http/types@0.2.8" "[method]outgoing-request.body"
    (func $req-body (param i32 i32)))
  (import "wasi:http/types@0.2.8" "[method]outgoing-body.write"
    (func $body-write (param i32 i32)))
  (import "wasi:http/types@0.2.8" "[method]incoming-response.consume"
    (func $resp-consume (param i32 i32)))
  (import "wasi:http/types@0.2.8" "[method]incoming-body.stream"
    (func $body-stream (param i32 i32)))
  (import "wasi:http/types@0.2.8" "[resource-drop]outgoing-body"
    (func $drop-out-body (param i32)))
  (import "wasi:http/types@0.2.8" "[resource-drop]incoming-body"
    (func $drop-in-body (param i32)))
  (import "wasi:io/streams@0.2.8" "[resource-drop]output-stream"
    (func $drop-out-stream (param i32)))
  (import "wasi:io/streams@0.2.8" "[resource-drop]input-stream"
    (func $drop-in-stream (param i32)))
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
  (func (export "ask-write-chain") (param i32) (result i32)
    (local $r i32) (local $disc i32)
    (local $bodyHandle i32) (local $streamHandle i32)
    ;; result<own<resource>, _>: 8 bytes (disc + 3 pad + 4 handle)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $req-body (local.get 0) (local.get $r))
    (local.set $disc (i32.load8_u (local.get $r)))
    (local.set $bodyHandle (i32.load offset=4 (local.get $r)))
    (call $body-write (local.get $bodyHandle) (local.get $r))
    (local.set $streamHandle (i32.load offset=4 (local.get $r)))
    (call $drop-out-stream (local.get $streamHandle))
    (call $drop-out-body (local.get $bodyHandle))
    (local.get $disc))
  (func (export "ask-read-chain") (param i32) (result i32)
    (local $r i32) (local $disc i32)
    (local $bodyHandle i32) (local $streamHandle i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $resp-consume (local.get 0) (local.get $r))
    (local.set $disc (i32.load8_u (local.get $r)))
    (local.set $bodyHandle (i32.load offset=4 (local.get $r)))
    (call $body-stream (local.get $bodyHandle) (local.get $r))
    (local.set $streamHandle (i32.load offset=4 (local.get $r)))
    (call $drop-in-stream (local.get $streamHandle))
    (call $drop-in-body (local.get $bodyHandle))
    (local.get $disc)))
