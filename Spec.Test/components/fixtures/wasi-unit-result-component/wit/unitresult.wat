(module
  (import "test:unit/iface@1.0.0" "[method]thing.do-it"
    (func $doIt (param i32) (result i32)))
  (import "test:unit/iface@1.0.0" "[method]thing.set-name"
    (func $setName (param i32 i32 i32 i32) (result i32)))
  (memory (export "memory") 1)
  (data (i32.const 256) "Alice")
  (func (export "ask-do-it") (param i32) (result i32)
    (call $doIt (local.get 0)))
  (func (export "ask-set-name-some") (param i32) (result i32)
    ;; set-name(thing, Some("Alice")): disc=1, ptr=256, len=5
    (call $setName (local.get 0) (i32.const 1) (i32.const 256) (i32.const 5)))
  (func (export "ask-set-name-none") (param i32) (result i32)
    ;; set-name(thing, None): disc=0, ptr=0, len=0
    (call $setName (local.get 0) (i32.const 0) (i32.const 0) (i32.const 0))))
