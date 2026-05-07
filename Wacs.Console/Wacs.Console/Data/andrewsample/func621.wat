  (func (;621;) (type 19) (param i32 i32 i32 i32 i32 i32 i32 i32 i32) (result i32)
    (local i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32)
    global.get 0
    i32.const 80
    i32.sub
    local.tee 10
    global.set 0
    block ;; label = @1
      block ;; label = @2
        block ;; label = @3
          block ;; label = @4
            local.get 2
            i32.const -41
            i32.and
            local.tee 9
            i32.eqz
            if ;; label = @5
              local.get 3
              i32.load
              local.tee 15
              i32.load offset=36
              local.set 14
              local.get 8
              i32.const 0
              i32.store
              block ;; label = @6
                local.get 14
                i32.eqz
                br_if 0 (;@6;)
                local.get 14
                i32.load offset=4
                i32.load
                local.tee 9
                i32.load offset=12
                local.tee 11
                i32.const 0
                i32.le_s
                br_if 0 (;@6;)
                local.get 2
                i32.const 32
                i32.and
                local.set 19
                local.get 2
                i32.const 8
                i32.and
                local.set 20
                local.get 9
                i32.load offset=4
                local.tee 16
                local.get 11
                i32.const 2
                i32.shl
                i32.add
                local.set 17
                i32.const 0
                local.set 9
                local.get 8
                block (result i32) ;; label = @7
                  loop ;; label = @8
                    local.get 9
                    local.set 12
                    local.get 11
                    local.set 2
                    block ;; label = @9
                      local.get 17
                      i32.load
                      local.tee 11
                      i32.eqz
                      br_if 0 (;@9;)
                      local.get 1
                      local.get 11
                      i32.load8_u offset=32
                      i32.ne
                      br_if 0 (;@9;)
                      block ;; label = @10
                        local.get 11
                        i32.load
                        local.tee 13
                        local.get 0
                        i32.eq
                        br_if 0 (;@10;)
                        local.get 13
                        local.get 0
                        local.get 1
                        call 2574
                        i32.eqz
                        br_if 0 (;@10;)
                        br 1 (;@9;)
                      end
                      local.get 2
                      local.set 9
                      local.get 11
                      i32.load8_u offset=33
                      local.tee 21
                      i32.const 1
                      i32.and
                      br_if 0 (;@9;)
                      local.get 11
                      i32.load offset=16
                      local.tee 13
                      i32.const -1
                      i32.eq
                      if ;; label = @10
                        local.get 12
                        local.set 9
                        br 1 (;@9;)
                      end
                      block ;; label = @10
                        block ;; label = @11
                          local.get 11
                          i32.load offset=20
                          local.tee 18
                          i32.const -1
                          i32.eq
                          if ;; label = @12
                            local.get 4
                            local.get 13
                            i32.le_u
                            br_if 1 (;@11;)
                            local.get 12
                            local.set 9
                            local.get 4
                            local.get 13
                            i32.sub
                            i32.const 2147483647
                            i32.ge_u
                            br_if 3 (;@9;)
                            br 2 (;@10;)
                          end
                          local.get 13
                          local.get 18
                          i32.gt_u
                          if ;; label = @12
                            local.get 4
                            local.get 13
                            i32.gt_u
                            br_if 2 (;@10;)
                            local.get 12
                            local.set 9
                            local.get 4
                            local.get 18
                            i32.gt_u
                            br_if 3 (;@9;)
                            br 2 (;@10;)
                          end
                          local.get 4
                          local.get 13
                          i32.le_u
                          if ;; label = @12
                            local.get 12
                            local.set 9
                            br 3 (;@9;)
                          end
                          local.get 12
                          local.set 9
                          local.get 4
                          local.get 18
                          i32.le_u
                          br_if 1 (;@10;)
                          br 2 (;@9;)
                        end
                        local.get 12
                        local.set 9
                        local.get 13
                        local.get 4
                        i32.sub
                        i32.const 0
                        i32.ge_s
                        br_if 1 (;@9;)
                      end
                      local.get 7
                      local.get 11
                      i32.store
                      local.get 19
                      i32.const 1
                      local.get 21
                      i32.const 32
                      i32.and
                      select
                      if ;; label = @10
                        i32.const 1
                        local.set 4
                        i32.const 1
                        local.get 3
                        i32.load
                        local.tee 12
                        i32.load offset=48
                        local.tee 9
                        i32.const 128
                        i32.and
                        br_if 3 (;@7;)
                        drop
                        i32.const 0
                        local.get 9
                        i32.const 256
                        i32.and
                        br_if 3 (;@7;)
                        drop
                        i32.const 0
                        local.get 12
                        i32.load offset=24
                        i32.eqz
                        i32.const 1
                        i32.shl
                        local.get 9
                        i32.const 4194304
                        i32.and
                        select
                        br 3 (;@7;)
                      end
                      local.get 10
                      local.get 11
                      i32.load
                      local.get 1
                      i32.const 537395200
                      call 1405
                      i32.store offset=48
                      i32.const 101879
                      local.get 10
                      i32.const 48
                      i32.add
                      call 1564
                      unreachable
                    end
                    local.get 17
                    i32.const 4
                    i32.sub
                    local.set 17
                    local.get 2
                    i32.const 1
                    i32.sub
                    local.set 11
                    local.get 2
                    i32.const 1
                    i32.gt_s
                    br_if 0 (;@8;)
                  end
                  local.get 9
                  i32.const 0
                  i32.le_s
                  br_if 1 (;@6;)
                  local.get 7
                  local.get 16
                  local.get 9
                  i32.const 2
                  i32.shl
                  i32.add
                  i32.load
                  local.tee 11
                  i32.store
                  i32.const 0
                  local.set 4
                  local.get 9
                  local.set 2
                  local.get 11
                  i32.load offset=20
                end
                local.tee 13
                i32.store
                local.get 6
                i32.eqz
                br_if 5 (;@1;)
                local.get 11
                i32.load offset=4
                if ;; label = @7
                  local.get 6
                  i32.const 0
                  i32.store
                  br 6 (;@1;)
                end
                local.get 3
                i32.load
                local.tee 9
                i32.load offset=48
                local.set 12
                block ;; label = @7
                  block ;; label = @8
                    local.get 9
                    i32.load offset=24
                    i32.eqz
                    if ;; label = @9
                      local.get 12
                      i32.const 4194304
                      i32.and
                      i32.eqz
                      br_if 1 (;@8;)
                      local.get 12
                      i32.const 224
                      i32.and
                      i32.const 160
                      i32.eq
                      br_if 2 (;@7;)
                      br 5 (;@4;)
                    end
                    local.get 12
                    i32.const 224
                    i32.and
                    i32.const 160
                    i32.ne
                    br_if 5 (;@3;)
                    br 1 (;@7;)
                  end
                  local.get 13
                  i32.const 1
                  i32.and
                  i32.eqz
                  br_if 3 (;@4;)
                end
                local.get 5
                i32.eqz
                br_if 4 (;@2;)
                local.get 11
                i32.load
                local.tee 3
                i32.load8_u
                local.set 4
                local.get 11
                i32.load8_u offset=32
                local.set 5
                local.get 10
                local.get 3
                i32.store offset=44
                local.get 10
                local.get 5
                i32.store offset=40
                local.get 10
                i32.const 1
                i32.store offset=36
                local.get 10
                i32.const 93536
                i32.const 97211
                local.get 4
                i32.const 38
                i32.eq
                select
                i32.store offset=32
                i32.const 1
                i32.const 96996
                local.get 10
                i32.const 32
                i32.add
                call 1589
                br 4 (;@2;)
              end
              local.get 15
              i32.load offset=40
              local.tee 5
              i32.eqz
              if ;; label = @6
                i32.const -1
                local.set 2
                br 5 (;@1;)
              end
              local.get 15
              i32.load offset=48
              local.set 4
              block ;; label = @6
                local.get 6
                br_if 0 (;@6;)
                i32.const 0
                local.set 6
                local.get 4
                i32.const 160
                i32.and
                br_if 0 (;@6;)
                local.get 10
                i32.const 76
                i32.add
                i32.const 0
                local.get 3
                i32.load8_u offset=8
                i32.const 14
                i32.ne
                select
                local.set 6
              end
              i32.const -1
              local.set 2
              local.get 0
              local.get 1
              local.get 4
              i32.const 15
              i32.shr_u
              i32.const 32
              i32.and
              local.get 6
              local.get 10
              i32.const 76
              i32.add
              i32.eq
              i32.const 3
              i32.shl
              i32.or
              local.get 5
              local.get 15
              i32.load offset=44
              i32.const 1
              local.get 6
              local.get 7
              local.get 8
              call 621
              local.tee 5
              i32.const -1
              i32.eq
              br_if 4 (;@1;)
              block ;; label = @6
                local.get 7
                i32.load
                local.tee 1
                i32.load8_u offset=33
                i32.const 32
                i32.and
                if ;; label = @7
                  local.get 1
                  i32.load offset=12
                  i32.load offset=8
                  local.tee 0
                  i32.const 4679016
                  i32.load
                  i32.ne
                  br_if 1 (;@6;)
                end
                i32.const 0
                local.set 2
                local.get 3
                i32.load
                local.tee 0
                i32.load offset=24
                br_if 5 (;@1;)
                local.get 14
                i32.eqz
                br_if 5 (;@1;)
                local.get 0
                i32.load offset=48
                i32.const 4194304
                i32.and
                br_if 5 (;@1;)
                i32.const 1
                i32.const 36
                call 1563
                local.tee 0
                i32.const 1
                i32.store offset=24
                local.get 0
                i32.const 1
                i32.store8 offset=33
                local.get 0
                local.get 1
                i32.load
                local.tee 2
                i32.store
                local.get 2
                i32.const 10
                i32.sub
                local.tee 2
                local.get 2
                i32.load
                i32.const 1
                i32.add
                i32.store
                local.get 1
                i32.load8_u offset=33
                i32.const 32
                i32.and
                if ;; label = @7
                  local.get 0
                  i32.const 33
                  i32.store8 offset=33
                  local.get 0
                  local.get 1
                  i32.load offset=12
                  local.tee 2
                  i32.store offset=12
                  local.get 2
                  local.get 2
                  i32.load
                  i32.const 1
                  i32.add
                  i32.store
                end
                local.get 0
                local.get 1
                i32.load8_u offset=32
                i32.store8 offset=32
                i32.const 4679984
                i32.load
                local.set 9
                i32.const 4679984
                local.get 14
                i32.load offset=4
                local.tee 1
                i32.load
                i32.store
                i32.const 4679052
                i32.load
                local.set 4
                i32.const 4679052
                local.get 1
                i32.load offset=4
                local.tee 1
                i32.store
                i32.const 4678928
                local.get 1
                i32.load offset=16
                i32.store
                local.get 0
                local.get 7
                i32.load
                local.tee 1
                i32.load8_u offset=33
                i32.const 2
                i32.and
                local.get 1
                i32.load offset=8
                local.get 1
                i32.load offset=4
                call 615
                local.set 2
                local.get 0
                i32.const 0
                i32.store offset=16
                local.get 0
                local.get 8
                i32.load
                local.tee 1
                i32.store offset=20
                block ;; label = @7
                  local.get 0
                  i32.load offset=4
                  br_if 0 (;@7;)
                  block ;; label = @8
                    local.get 3
                    i32.load
                    local.tee 12
                    i32.load offset=48
                    local.tee 11
                    i32.const 160
                    i32.and
                    i32.eqz
                    if ;; label = @9
                      local.get 3
                      i32.load8_u offset=8
                      i32.const 14
                      i32.ne
                      br_if 1 (;@8;)
                    end
                    local.get 0
                    local.get 5
                    i32.store offset=16
                    local.get 12
                    local.get 11
                    i32.const 32
                    i32.or
                    i32.store offset=48
                    br 1 (;@7;)
                  end
                  i32.const 4679052
                  i32.load
                  local.set 22
                  local.get 6
                  i32.load
                  local.tee 1
                  if ;; label = @8
                    local.get 1
                    local.get 1
                    i32.load offset=4
                    i32.const 1
                    i32.add
                    i32.store offset=4
                  end
                  local.get 22
                  local.get 2
                  local.get 1
                  call 291
                  drop
                  local.get 0
                  local.get 5
                  i32.store offset=16
                  local.get 0
                  i32.load offset=20
                  local.set 1
                end
                local.get 7
                local.get 0
                i32.store
                local.get 8
                local.get 1
                i32.store
                i32.const 4679984
                local.get 9
                i32.store
                i32.const 4679052
                local.get 4
                i32.store
                i32.const 4678928
                local.get 4
                if (result i32) ;; label = @7
                  local.get 4
                  i32.load offset=16
                else
                  i32.const 0
                end
                i32.store
                br 5 (;@1;)
              end
              local.get 10
              local.get 1
              i32.load
              local.get 1
              i32.load8_u offset=32
              i32.const 537395200
              call 1405
              i32.store
              local.get 10
              local.get 0
              i32.store offset=4
              local.get 10
              i32.const 4679016
              i32.load
              i32.store offset=8
              i32.const 64199
              local.get 10
              call 1564
              unreachable
            end
            local.get 10
            local.get 9
            i32.store offset=64
            i32.const 23806
            local.get 10
            i32.const -64
            i32.sub
            call 1564
            unreachable
          end
          local.get 12
          i32.const 4194304
          i32.and
          br_if 0 (;@3;)
          local.get 13
          i32.const 2
          i32.and
          i32.eqz
          br_if 0 (;@3;)
          local.get 5
          i32.eqz
          br_if 0 (;@3;)
          local.get 16
          local.get 2
          i32.const 2
          i32.shl
          i32.add
          i32.load
          i32.load8_u offset=33
          i32.const 2
          i32.and
          br_if 0 (;@3;)
          i32.const 1
          local.set 5
          i32.const 1
          call 1590
          i32.eqz
          br_if 0 (;@3;)
          local.get 0
          i32.load8_u
          local.set 5
          local.get 10
          local.get 0
          i32.store offset=28
          local.get 10
          local.get 1
          i32.store offset=24
          local.get 10
          i32.const 1
          i32.store offset=20
          local.get 10
          i32.const 93536
          i32.const 97211
          local.get 5
          i32.const 38
          i32.eq
          select
          i32.store offset=16
          i32.const 1
          i32.const 106883
          local.get 10
          i32.const 16
          i32.add
          call 1591
          local.get 3
          i32.load
          local.set 9
          i32.const 0
          local.set 5
        end
        block ;; label = @3
          local.get 4
          br_if 0 (;@3;)
          local.get 9
          i32.load offset=48
          i32.const 224
          i32.and
          i32.const 160
          i32.ne
          br_if 0 (;@3;)
          local.get 7
          i32.load
          local.set 3
          local.get 0
          local.get 1
          i32.const 0
          local.get 9
          i32.load offset=40
          local.get 9
          i32.load offset=44
          local.get 5
          local.get 6
          local.get 7
          local.get 8
          call 621
          drop
          local.get 7
          local.get 3
          i32.store
          br 2 (;@1;)
        end
        local.get 6
        local.get 14
        i32.load offset=4
        i32.const 1
        local.get 9
        i32.load offset=52
        local.tee 4
        local.get 4
        i32.const 1
        i32.le_u
        select
        i32.const 2
        i32.shl
        i32.add
        i32.load
        i32.load offset=16
        local.get 2
        i32.const 2
        i32.shl
        i32.add
        i32.load
        local.tee 4
        i32.store
        local.get 4
        i32.load8_u offset=10
        i32.const 4
        i32.and
        i32.eqz
        br_if 1 (;@1;)
        local.get 20
        if ;; label = @3
          local.get 3
          i32.load
          i32.load offset=52
          br_if 2 (;@1;)
        end
        local.get 16
        local.get 2
        i32.const 2
        i32.shl
        i32.add
        i32.load
        local.tee 4
        i32.load8_u offset=33
        i32.const 2
        i32.and
        br_if 1 (;@1;)
        global.get 0
        i32.const 16
        i32.sub
        local.tee 3
        global.set 0
        local.get 4
        i32.load
        local.tee 5
        i32.load8_u
        local.set 7
        local.get 4
        i32.load8_u offset=32
        local.set 4
        local.get 3
        local.get 5
        i32.store offset=12
        local.get 3
        local.get 4
        i32.store offset=8
        local.get 3
        i32.const 1
        i32.store offset=4
        local.get 3
        i32.const 93536
        i32.const 97211
        local.get 7
        i32.const 38
        i32.eq
        select
        i32.store
        i32.const 1
        i32.const 96996
        local.get 3
        call 1589
        local.get 3
        i32.const 16
        i32.add
        global.set 0
      end
      local.get 6
      i32.const 0
      i32.store
      block ;; label = @2
        local.get 1
        i32.eqz
        br_if 0 (;@2;)
        block ;; label = @3
          block ;; label = @4
            block ;; label = @5
              local.get 0
              i32.load8_u
              i32.const 37
              i32.sub
              br_table 1 (;@4;) 2 (;@3;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 3 (;@2;) 0 (;@5;) 3 (;@2;)
            end
            local.get 6
            i32.const 11
            call 622
            i32.store
            br 3 (;@1;)
          end
          local.get 6
          i32.const 12
          call 622
          i32.store
          br 2 (;@1;)
        end
        local.get 6
        i32.const 13
        call 622
        i32.store
        br 1 (;@1;)
      end
      local.get 6
      i32.const 0
      call 622
      i32.store
    end
    local.get 10
    i32.const 80
    i32.add
    global.set 0
    local.get 2
  )