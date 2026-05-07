(module
  (type (;0;) (func))
  (func (;0;) (type 0)
    (local i32)
    i32.const 666
    local.set 0
    local.get 0
    i32.const 1
    i32.add
    local.set 0
  )
  (export "main" (func 0))
  (start 0)
)