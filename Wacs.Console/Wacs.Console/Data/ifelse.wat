(module
  (type (;0;) (func (param i32) (result i32)))
  (type (;1;) (func (result i32)))
  (func (;0;) (type 0)
    (if (local.get 0)
      (then
        (i32.const 666)
        (local.set 0)
      )
    )
    
    (if (type 1) (local.get 0)
      (then
        i32.const 2  
      )
      (else 
        i32.const 1
      )
    )
    (i32.sub (i32.const 1))
  )
  (export "main" (func 0))
)