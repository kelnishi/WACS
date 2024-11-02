(module
  (type (;0;) (func (result i32)))
  (func (;0;) (type 0) (result i32)
    (local i32)
    block (result i32)  ;; label = @1
      block (result i32)  ;; label = @2
        block (result i32)  ;; label = @3
          block (result i32)  ;; label = @4
            block  ;; label = @5
              block  ;; label = @6
                block  ;; label = @7
                  i32.const 8
                  local.set 0
                  loop  ;; label = @8
                    local.get 0
                    i32.const 1
                    i32.sub
                    local.tee 0
                    i32.const 3
                    i32.gt_s
                    br_if 0 (;@8;)
                  end
                  local.get 0
                  br 3 (;@4;)
                end
                unreachable
              end
              unreachable
            end
            unreachable
          end
          return
        end
        unreachable
      end
      unreachable
    end)
  (export "main" (func 0))
)
