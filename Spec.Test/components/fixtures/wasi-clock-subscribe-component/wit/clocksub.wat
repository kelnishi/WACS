(module
  (import "wasi:clocks/monotonic-clock@0.2.3" "subscribe-instant"
    (func $si (param i64) (result i32)))
  (import "wasi:clocks/monotonic-clock@0.2.3" "subscribe-duration"
    (func $sd (param i64) (result i32)))
  (import "wasi:io/poll@0.2.3" "[resource-drop]pollable"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (func (export "ask-subscribe") (result i32)
    (local $a i32) (local $b i32)
    (local.set $a (call $si (i64.const 1000)))
    (local.set $b (call $sd (i64.const 2000)))
    (call $drop (local.get $a))
    (call $drop (local.get $b))
    (i32.const 0)))
