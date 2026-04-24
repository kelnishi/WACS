(module
  (memory (export "memory") 1)
  (data (i32.const 0) "\2a\00\00\00")
  (func (export "ping") (result i32)
    i32.const 0
    i32.load))
