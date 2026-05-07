(module
  (type (;0;) (func (result i32)))
  (func (;0;) (type 0) (result i32)
    (local i32)
    i32.const 10
    local.set 0
    block (result i32)  ;; label = @1
      loop  ;; label = @2
        local.get 0
        i32.const 2
        i32.sub
        local.set 0
        local.get 0
        i32.const 4
        i32.gt_s
        br_if 0 (;@2;)
      end (;< @2 ;)
      local.get 0
    end (;< @1 ;))
  (export "main" (func 0))
)
