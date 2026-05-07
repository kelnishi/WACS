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
                  i32.const 10
                  local.set 0
                  loop  ;; label = @8
                    local.get 0
                    i32.const 2
                    i32.sub
                    local.tee 0
                    i32.const 4
                    i32.gt_s 
                    br_if 0 (;@8;) ;; while local[0]>4 continue
                  end (;< @8 ;)
                  local.get 0
                  br 3 (;@4;)
                end (;< @7 ;)
                unreachable
              end (;< @6 ;)
              unreachable
            end (;< @5 ;)
            unreachable
          end (;< @4 ;)
          return
        end (;< @3 ;)
        unreachable
      end (;< @2 ;)
      unreachable
    end (;< @1 ;))
  (export "main" (func 0))
)
