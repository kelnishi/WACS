(module
  (import "wasi:clocks/monotonic-clock@0.2.8" "now"
    (func $now (result i64)))
  (import "wasi:clocks/monotonic-clock@0.2.8" "resolution"
    (func $resolution (result i64)))
  (memory (export "memory") 1)
  (func (export "elapsed") (result i64) call $now)
  (func (export "step") (result i64) call $resolution))
