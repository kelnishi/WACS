(module
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 100)
  (export "cabi_realloc" (func $realloc))
  (func (export "[constructor]counter") (result i32) i32.const 1)
  (func (export "[method]counter.value") (param i32) (result i32) i32.const 42)
  (func (export "[resource-drop]counter") (param i32)
    nop)
  (func (export "make") (result i32) i32.const 1))
