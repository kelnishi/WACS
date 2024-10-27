(module
  (type (;0;) (func (param i32) (result i32)))
  (func (;0;) (type 0) (param i32) (result i32)
    (local i32)
    i32.const 1
    local.set 1
    block  ;; label = @1
      local.get 0
      i32.eqz
      br_if 0 (;@1;)
      loop  ;; label = @2
        local.get 1
        local.get 0
        i32.mul
        local.set 1
        local.get 0
        i32.const -1
        i32.add
        local.tee 0
        i32.eqz
        br_if 1 (;@1;)
        br 0 (;@2;)
      end
    end
    local.get 1)
  (export "iterFact" (func 0)))
