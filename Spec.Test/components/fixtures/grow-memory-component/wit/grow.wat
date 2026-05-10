(module
  (memory (export "memory") 1)
  (func (export "grow-big") (result i32)
    i32.const 50000
    memory.grow))
