(module
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 100)
  (export "cabi_realloc" (func $realloc))
  ;; Constructor — returns the (constant) handle 1.
  (func (export "[constructor]counter") (result i32) i32.const 1)
  ;; Instance methods — first param is the handle.
  (func (export "[method]counter.value") (param i32) (result i32)
    i32.const 42)
  (func (export "[method]counter.add") (param i32 i32) (result i32)
    local.get 1
    i32.const 100
    i32.add)
  ;; Drop — no-op for our static handle.
  (func (export "[resource-drop]counter") (param i32) nop)
  ;; World-level exports.
  (func (export "make") (result i32) i32.const 1)
  ;; inspect: takes a borrowed handle and returns its value.
  ;; Wired to the same constant 42 as `value` for the simple fixture.
  (func (export "inspect") (param i32) (result i32) i32.const 42))
