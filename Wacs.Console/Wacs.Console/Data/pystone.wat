(module
  (type (;0;) (func (result i32)))
  (type (;1;) (func (param i64) (result i32)))
  (type (;2;) (func (param i32 i64 i32)))
  (type (;3;) (func (param i32 i64 i32 i64 i32)))
  (type (;4;) (func (param i32 i32 i32) (result i32)))
  (type (;5;) (func (param i32)))
  (type (;6;) (func (param i64 i32)))
  (type (;7;) (func (param i64 i32 i32)))
  (type (;8;) (func (param i32 i32) (result i32)))
  (type (;9;) (func (param i32 i64 i64) (result i32)))
  (type (;10;) (func (param i32 i64 i32 i64 i32 i64 i32 i64 i32)))
  (type (;11;) (func (param i64)))
  (type (;12;) (func (result i64)))
  (type (;13;) (func (param i32 i64 i64 i32 i32)))
  (type (;14;) (func (param i32 f64 i64 i32 i32)))
  (type (;15;) (func (param i32) (result i64)))
  (type (;16;) (func (param i32) (result i32)))
  (type (;17;) (func (param i64 i32 i32 i32) (result i64)))
  (import "env" "seq_alloc" (func (;0;) (type 1)))
  (import "env" "seq_alloc_atomic" (func (;1;) (type 1)))
  (import "env" "memcpy" (func (;2;) (type 4)))
  (import "env" "seq_print_full" (func (;3;) (type 7)))
  (import "env" "seq_alloc_exc" (func (;4;) (type 8)))
  (import "env" "seq_throw" (func (;5;) (type 5)))
  (import "env" "__stack_pointer" (global (;0;) (mut i32)))
  (import "env" "seq_realloc" (func (;6;) (type 9)))
  (import "env" "seq_time_highres" (func (;7;) (type 12)))
  (import "env" "seq_str_int" (func (;8;) (type 13)))
  (import "env" "seq_str_float" (func (;9;) (type 14)))
  (import "env" "strlen" (func (;10;) (type 15)))
  (import "env" "seq_init" (func (;11;) (type 5)))
  (import "env" "seq_stdout" (func (;12;) (type 0)))
  (import "env" "seq_stdin" (func (;13;) (type 0)))
  (import "env" "seq_stderr" (func (;14;) (type 0)))
  (import "env" "isspace" (func (;15;) (type 16)))
  (import "env" "seq_int_from_str" (func (;16;) (type 17)))
  (import "env" "__indirect_function_table" (table (;0;) 0 funcref))
  (func $.Lstd.internal.types.error.IndexError.__new__:0.305 (type 0) (result i32)
    i64.const 88
    call 0)
  (func $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349 (type 2) (param i32 i64 i32)
    local.get 0
    i64.const 10
    i64.store
    local.get 0
    i32.const 44
    i32.store offset=8
    local.get 0
    local.get 1
    i64.store offset=16
    local.get 0
    local.get 2
    i32.store offset=24
    local.get 0
    i64.const 0
    i64.store offset=32
    local.get 0
    i32.const 915
    i32.store offset=40
    local.get 0
    i64.const 0
    i64.store offset=48
    local.get 0
    i32.const 915
    i32.store offset=56
    local.get 0
    i64.const 0
    i64.store offset=64
    local.get 0
    i64.const 0
    i64.store offset=72
    local.get 0
    i32.const 0
    i32.store offset=80)
  (func $.Lstd.internal.types.error.ValueError.__new__:0.526 (type 0) (result i32)
    i64.const 88
    call 0)
  (func $.Lstd.internal.types.error.ValueError:std.internal.types.error.ValueError.__init__:3_std.internal.types.error.ValueError_str_.535 (type 2) (param i32 i64 i32)
    local.get 0
    i64.const 10
    i64.store
    local.get 0
    i32.const 55
    i32.store offset=8
    local.get 0
    local.get 1
    i64.store offset=16
    local.get 0
    local.get 2
    i32.store offset=24
    local.get 0
    i64.const 0
    i64.store offset=32
    local.get 0
    i32.const 915
    i32.store offset=40
    local.get 0
    i64.const 0
    i64.store offset=48
    local.get 0
    i32.const 915
    i32.store offset=56
    local.get 0
    i64.const 0
    i64.store offset=64
    local.get 0
    i64.const 0
    i64.store offset=72
    local.get 0
    i32.const 0
    i32.store offset=80)
  (func $.Lstr.cat:0_Tuple_str_str__.662 (type 3) (param i32 i64 i32 i64 i32)
    (local i64 i32)
    local.get 3
    local.get 1
    i64.add
    local.tee 5
    call 1
    local.get 2
    local.get 1
    i32.wrap_i64
    local.tee 6
    call 2
    local.tee 2
    local.get 6
    i32.add
    local.get 4
    local.get 3
    i32.wrap_i64
    call 2
    drop
    local.get 0
    local.get 2
    i32.store offset=8
    local.get 0
    local.get 5
    i64.store)
  (func $.Lstd.internal.types.error.SystemExit.__new__:0.730 (type 0) (result i32)
    i64.const 96
    call 0)
  (func $.Lstd.internal.types.error.SystemExit:std.internal.types.error.SystemExit.__init__:3_std.internal.types.error.SystemExit_int_.743 (type 5) (param i32)
    local.get 0
    i64.const 10
    i64.store
    local.get 0
    i32.const 212
    i32.store offset=8
    local.get 0
    i64.const 0
    i64.store offset=16
    local.get 0
    i32.const 915
    i32.store offset=24
    local.get 0
    i64.const 0
    i64.store offset=32
    local.get 0
    i32.const 915
    i32.store offset=40
    local.get 0
    i64.const 0
    i64.store offset=48
    local.get 0
    i32.const 915
    i32.store offset=56
    local.get 0
    i64.const 0
    i64.store offset=64
    local.get 0
    i64.const 0
    i64.store offset=72
    local.get 0
    i32.const 0
    i32.store offset=80
    local.get 0
    i64.const 100
    i64.store offset=88)
  (func $.Lerror.5:0_str_.754 (type 6) (param i64 i32)
    (local i32)
    local.get 0
    local.get 1
    i32.const 0
    i32.load offset=1160
    local.tee 2
    call 3
    i64.const 1
    i32.const 280
    local.get 2
    call 3
    block  ;; label = @1
      i32.const 0
      i64.load offset=1136
      i64.const 0
      i64.gt_s
      br_if 0 (;@1;)
      call $.Lstd.internal.types.error.IndexError.__new__:0.305
      local.tee 1
      i64.const 23
      i32.const 848
      call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
      local.get 1
      i64.const 40
      i64.store offset=32
      local.get 1
      i32.const 672
      i32.store offset=40
      local.get 1
      i64.const 67
      i64.store offset=48
      local.get 1
      i32.const 720
      i32.store offset=56
      local.get 1
      i64.const 374
      i64.store offset=64
      local.get 1
      i64.const 13
      i64.store offset=72
      i32.const 1000
      local.get 1
      call 4
      call 5
      unreachable
    end
    i32.const 0
    i32.load offset=1152
    local.tee 1
    i32.load offset=8
    local.set 2
    local.get 1
    i64.load
    local.set 0
    i64.const 7
    i32.const 282
    i32.const 0
    i32.load offset=1160
    local.tee 1
    call 3
    local.get 0
    local.get 2
    local.get 1
    call 3
    i64.const 18
    i32.const 304
    local.get 1
    call 3
    i64.const 1
    i32.const 1009
    local.get 1
    call 3
    call $.Lstd.internal.types.error.SystemExit.__new__:0.730
    local.tee 1
    call $.Lstd.internal.types.error.SystemExit:std.internal.types.error.SystemExit.__init__:3_std.internal.types.error.SystemExit_int_.743
    local.get 1
    i64.const 14
    i64.store offset=32
    local.get 1
    i32.const 223
    i32.store offset=40
    local.get 1
    i64.const 39
    i64.store offset=48
    local.get 1
    i32.const 240
    i32.store offset=56
    local.get 1
    i64.const 10
    i64.store offset=64
    local.get 1
    i64.const 5
    i64.store offset=72
    i32.const 1002
    local.get 1
    call 4
    call 5
    unreachable)
  (func $.Lstr:str.__repr__:0_str_.821 (type 2) (param i32 i64 i32)
    (local i32 i64 i32 i32 i32 i64 i32 i32 i64 i32 i64 i32 i64 i64 i64)
    global.get 0
    i32.const 16
    i32.sub
    local.tee 3
    global.set 0
    local.get 1
    i64.const 2
    i64.add
    local.tee 4
    call 1
    local.set 5
    block  ;; label = @1
      block  ;; label = @2
        local.get 1
        i64.const 1
        i64.ge_s
        br_if 0 (;@2;)
        i32.const 0
        local.set 6
        i32.const 0
        local.set 7
        br 1 (;@1;)
      end
      local.get 1
      i64.const -1
      i64.add
      local.set 8
      i32.const 0
      local.set 9
      local.get 2
      local.set 10
      i32.const 0
      local.set 6
      loop  ;; label = @2
        i32.const 1
        local.set 7
        block  ;; label = @3
          block  ;; label = @4
            block  ;; label = @5
              local.get 10
              i32.load8_u
              i32.const -34
              i32.add
              br_table 0 (;@5;) 1 (;@4;) 1 (;@4;) 1 (;@4;) 1 (;@4;) 2 (;@3;) 1 (;@4;)
            end
            i32.const 1
            local.set 6
          end
          local.get 9
          local.set 7
        end
        local.get 8
        i64.eqz
        br_if 1 (;@1;)
        local.get 8
        i64.const -1
        i64.add
        local.set 8
        local.get 10
        i32.const 1
        i32.add
        local.set 10
        local.get 7
        local.set 9
        br 0 (;@2;)
      end
    end
    i32.const 328
    i32.const 323
    local.get 7
    local.get 6
    i32.const 1
    i32.and
    i32.eqz
    i32.and
    local.tee 10
    select
    local.set 6
    block  ;; label = @1
      block  ;; label = @2
        local.get 4
        i64.const 0
        i64.le_s
        br_if 0 (;@2;)
        i64.const 0
        local.set 11
        br 1 (;@1;)
      end
      local.get 4
      local.set 11
      loop  ;; label = @2
        local.get 11
        local.tee 8
        i64.const 1
        i64.shl
        local.set 11
        i64.const 1
        local.get 8
        i64.gt_s
        br_if 0 (;@2;)
      end
      i64.const 0
      local.set 11
      local.get 5
      local.get 8
      local.get 4
      call 6
      local.set 5
      local.get 8
      local.set 4
    end
    local.get 5
    local.get 11
    i32.wrap_i64
    i32.add
    local.get 6
    i64.const 1
    i32.wrap_i64
    local.tee 12
    call 2
    drop
    block  ;; label = @1
      block  ;; label = @2
        local.get 1
        i64.const 1
        i64.ge_s
        br_if 0 (;@2;)
        i64.const 1
        local.set 13
        br 1 (;@1;)
      end
      i32.const 330
      i32.const 325
      local.get 10
      select
      local.set 14
      local.get 3
      i32.const 1
      i32.store8 offset=12
      i64.const 0
      local.set 15
      local.get 2
      local.set 7
      i64.const 1
      local.set 16
      loop  ;; label = @2
        i32.const 333
        local.set 10
        i64.const 2
        local.set 17
        block  ;; label = @3
          block  ;; label = @4
            block  ;; label = @5
              block  ;; label = @6
                block  ;; label = @7
                  block  ;; label = @8
                    block  ;; label = @9
                      block  ;; label = @10
                        block  ;; label = @11
                          local.get 7
                          i32.load8_u
                          local.tee 9
                          i32.const -9
                          i32.add
                          br_table 3 (;@8;) 6 (;@5;) 1 (;@10;) 1 (;@10;) 2 (;@9;) 0 (;@11;)
                        end
                        local.get 9
                        i32.const 92
                        i32.eq
                        br_if 3 (;@7;)
                      end
                      block  ;; label = @10
                        i64.const 1
                        i64.eqz
                        br_if 0 (;@10;)
                        local.get 9
                        local.get 6
                        i32.load8_u
                        i32.ne
                        br_if 0 (;@10;)
                        i64.const 2
                        local.set 17
                        local.get 14
                        local.set 10
                        i64.const 2
                        i64.eqz
                        i32.eqz
                        br_if 5 (;@5;)
                        local.get 16
                        local.set 13
                        br 6 (;@4;)
                      end
                      i64.const 1
                      local.set 17
                      local.get 7
                      local.set 10
                      local.get 9
                      i32.const -32
                      i32.add
                      i32.const 255
                      i32.and
                      i32.const 95
                      i32.lt_u
                      br_if 4 (;@5;)
                      local.get 4
                      local.set 8
                      block  ;; label = @10
                        local.get 4
                        local.get 16
                        i64.const 2
                        i64.add
                        local.tee 13
                        i64.ge_s
                        br_if 0 (;@10;)
                        loop  ;; label = @11
                          local.get 8
                          local.tee 11
                          i64.const 1
                          i64.shl
                          local.set 8
                          local.get 13
                          local.get 11
                          i64.gt_s
                          br_if 0 (;@11;)
                        end
                        local.get 5
                        local.get 11
                        local.get 4
                        call 6
                        local.set 5
                        local.get 11
                        local.set 4
                      end
                      local.get 5
                      local.get 16
                      i32.wrap_i64
                      i32.add
                      i32.const 30812
                      i32.store16 align=1
                      local.get 9
                      i32.const 4
                      i32.shr_u
                      local.set 10
                      block  ;; label = @10
                        block  ;; label = @11
                          local.get 4
                          local.get 16
                          i64.const 3
                          i64.add
                          local.tee 17
                          i64.lt_s
                          br_if 0 (;@11;)
                          local.get 4
                          local.set 8
                          br 1 (;@10;)
                        end
                        local.get 4
                        local.set 11
                        loop  ;; label = @11
                          local.get 11
                          local.tee 8
                          i64.const 1
                          i64.shl
                          local.set 11
                          local.get 17
                          local.get 8
                          i64.gt_s
                          br_if 0 (;@11;)
                        end
                        local.get 5
                        local.get 8
                        local.get 4
                        call 6
                        local.set 5
                      end
                      local.get 5
                      local.get 13
                      i32.wrap_i64
                      i32.add
                      local.get 10
                      i32.const 352
                      i32.add
                      i32.load8_u
                      i32.store8
                      local.get 9
                      i32.const 15
                      i32.and
                      local.set 10
                      block  ;; label = @10
                        block  ;; label = @11
                          local.get 8
                          local.get 16
                          i64.const 4
                          i64.add
                          local.tee 13
                          i64.lt_s
                          br_if 0 (;@11;)
                          local.get 8
                          local.set 4
                          br 1 (;@10;)
                        end
                        local.get 8
                        local.set 11
                        loop  ;; label = @11
                          local.get 11
                          local.tee 4
                          i64.const 1
                          i64.shl
                          local.set 11
                          local.get 13
                          local.get 4
                          i64.gt_s
                          br_if 0 (;@11;)
                        end
                        local.get 5
                        local.get 4
                        local.get 8
                        call 6
                        local.set 5
                      end
                      local.get 5
                      local.get 17
                      i32.wrap_i64
                      i32.add
                      local.get 10
                      i32.const 352
                      i32.add
                      i32.load8_u
                      i32.store8
                      local.get 3
                      i32.load8_u offset=12
                      br_if 5 (;@4;)
                      i64.const 0
                      local.set 15
                      br 6 (;@3;)
                    end
                    i32.const 336
                    local.set 10
                    br 2 (;@6;)
                  end
                  i32.const 339
                  local.set 10
                  br 1 (;@6;)
                end
                i32.const 342
                local.set 10
              end
              i64.const 2
              local.set 17
            end
            local.get 4
            local.set 8
            block  ;; label = @5
              local.get 4
              local.get 17
              local.get 16
              i64.add
              local.tee 13
              i64.ge_s
              br_if 0 (;@5;)
              loop  ;; label = @6
                local.get 8
                local.tee 11
                i64.const 1
                i64.shl
                local.set 8
                local.get 13
                local.get 11
                i64.gt_s
                br_if 0 (;@6;)
              end
              local.get 5
              local.get 11
              local.get 4
              call 6
              local.set 5
              local.get 11
              local.set 4
            end
            local.get 5
            local.get 16
            i32.wrap_i64
            i32.add
            local.get 10
            local.get 17
            i32.wrap_i64
            call 2
            drop
          end
          local.get 15
          i64.const 1
          i64.add
          local.tee 15
          local.get 1
          i64.eq
          br_if 2 (;@1;)
        end
        local.get 3
        i32.const 1
        i32.store8 offset=12
        local.get 2
        local.get 15
        i32.wrap_i64
        i32.add
        local.set 7
        local.get 13
        local.set 16
        br 0 (;@2;)
      end
    end
    block  ;; label = @1
      local.get 4
      local.get 13
      i64.const 1
      i64.add
      local.tee 17
      i64.ge_s
      br_if 0 (;@1;)
      local.get 4
      local.set 8
      loop  ;; label = @2
        local.get 8
        local.tee 11
        i64.const 1
        i64.shl
        local.set 8
        local.get 17
        local.get 11
        i64.gt_s
        br_if 0 (;@2;)
      end
      local.get 5
      local.get 11
      local.get 4
      call 6
      local.set 5
    end
    local.get 5
    local.get 13
    i32.wrap_i64
    i32.add
    local.get 6
    local.get 12
    call 2
    drop
    local.get 0
    local.get 5
    i32.store offset=8
    local.get 0
    local.get 17
    i64.store
    local.get 3
    i32.const 16
    i32.add
    global.set 0)
  (func $.Lstr.cat:0_Tuple_str_str_str_str__.839 (type 10) (param i32 i64 i32 i64 i32 i64 i32 i64 i32)
    (local i64 i32 i64 i32)
    i64.const 0
    local.set 9
    i32.const 1
    local.set 10
    local.get 1
    local.set 11
    block  ;; label = @1
      loop  ;; label = @2
        local.get 9
        local.get 11
        i64.add
        local.set 9
        local.get 10
        i32.const 7
        i32.and
        local.set 12
        i32.const 2
        local.set 10
        local.get 3
        local.set 11
        block  ;; label = @3
          block  ;; label = @4
            local.get 12
            i32.const -1
            i32.add
            br_table 2 (;@2;) 0 (;@4;) 1 (;@3;) 3 (;@1;) 2 (;@2;)
          end
          i32.const 3
          local.set 10
          local.get 5
          local.set 11
          br 1 (;@2;)
        end
        i32.const 4
        local.set 10
        local.get 7
        local.set 11
        br 0 (;@2;)
      end
    end
    local.get 9
    call 1
    local.get 2
    local.get 1
    i32.wrap_i64
    call 2
    local.set 2
    i32.const 2
    local.set 10
    block  ;; label = @1
      loop  ;; label = @2
        local.get 2
        local.get 1
        i32.wrap_i64
        i32.add
        local.get 4
        local.get 3
        i32.wrap_i64
        call 2
        drop
        local.get 1
        local.get 3
        i64.add
        local.set 1
        local.get 10
        i32.const 7
        i32.and
        local.set 12
        i32.const 3
        local.set 10
        local.get 6
        local.set 4
        local.get 5
        local.set 3
        block  ;; label = @3
          local.get 12
          i32.const -2
          i32.add
          br_table 1 (;@2;) 0 (;@3;) 2 (;@1;) 1 (;@2;)
        end
        i32.const 4
        local.set 10
        local.get 8
        local.set 4
        local.get 7
        local.set 3
        br 0 (;@2;)
      end
    end
    local.get 0
    local.get 2
    i32.store offset=8
    local.get 0
    local.get 9
    i64.store)
  (func $main (type 11) (param i64)
    (local i32 i64 i64 i32 i32 i64 i32 i64 i64 i64 i64 i64 i64 i32 i32 i32 i64 i32 i64 i64 i64 i32 f64 f64)
    global.get 0
    i32.const 80
    i32.sub
    local.tee 1
    global.set 0
    call 7
    local.set 2
    call 7
    local.set 3
    i64.const 48
    call 0
    local.tee 4
    i32.const 915
    i32.store offset=40
    local.get 4
    i64.const 0
    i64.store offset=32
    local.get 4
    i64.const 0
    i64.store offset=24
    local.get 4
    i64.const 0
    i64.store offset=16
    local.get 4
    i64.const 0
    i64.store offset=8
    local.get 4
    i32.const 0
    i32.store
    i32.const 0
    i64.const 48
    call 0
    local.tee 5
    i32.store offset=32
    local.get 5
    local.get 4
    i32.store
    local.get 5
    i64.const 40
    i64.store offset=24
    local.get 5
    i64.const 30
    i64.store offset=32
    local.get 5
    i32.const 880
    i32.store offset=40
    local.get 5
    i32.const 0
    i64.load8_u offset=24
    i64.store offset=8
    local.get 5
    i64.const 3
    i64.const 0
    i32.const 0
    i32.load8_u offset=26
    select
    i64.store offset=16
    block  ;; label = @1
      block  ;; label = @2
        block  ;; label = @3
          block  ;; label = @4
            block  ;; label = @5
              block  ;; label = @6
                block  ;; label = @7
                  block  ;; label = @8
                    block  ;; label = @9
                      block  ;; label = @10
                        i32.const 0
                        i64.load offset=1104
                        i64.const 8
                        i64.le_s
                        br_if 0 (;@10;)
                        i32.const 0
                        i32.load offset=1120
                        i32.const 32
                        i32.add
                        i32.load
                        local.tee 4
                        i64.load
                        i64.const 7
                        i64.le_s
                        br_if 1 (;@9;)
                        local.get 4
                        i32.const 16
                        i32.add
                        i32.load
                        i32.const 56
                        i32.add
                        i64.const 10
                        i64.store align=4
                        call 7
                        local.set 6
                        block  ;; label = @11
                          block  ;; label = @12
                            block  ;; label = @13
                              block  ;; label = @14
                                block  ;; label = @15
                                  block  ;; label = @16
                                    block  ;; label = @17
                                      local.get 0
                                      i64.const 1
                                      i64.lt_s
                                      br_if 0 (;@17;)
                                      i32.const 0
                                      i32.load offset=20
                                      local.set 7
                                      i32.const 0
                                      i64.load offset=1104
                                      local.tee 8
                                      i64.const 9
                                      i64.lt_s
                                      br_if 1 (;@16;)
                                      i32.const 0
                                      i32.load offset=1120
                                      local.set 4
                                      local.get 8
                                      i64.const 29
                                      i64.lt_u
                                      br_if 10 (;@7;)
                                      i64.const 5
                                      i64.const 0
                                      i32.const 0
                                      i32.load8_u offset=28
                                      select
                                      local.set 9
                                      i64.const 4
                                      i64.const 0
                                      i32.const 0
                                      i32.load8_u offset=27
                                      select
                                      local.set 10
                                      i64.const 3
                                      i64.const 0
                                      i32.const 0
                                      i32.load8_u offset=26
                                      select
                                      local.set 11
                                      i64.const 2
                                      i64.const 0
                                      i32.const 0
                                      i32.load8_u offset=25
                                      select
                                      local.set 12
                                      i32.const 0
                                      i64.load8_u offset=24
                                      local.set 13
                                      i32.const 0
                                      i32.load offset=32
                                      local.set 5
                                      local.get 7
                                      i32.const 16
                                      i32.add
                                      local.set 14
                                      local.get 4
                                      i32.const 32
                                      i32.add
                                      local.set 15
                                      local.get 4
                                      i32.const 112
                                      i32.add
                                      local.set 16
                                      local.get 0
                                      local.set 17
                                      loop  ;; label = @18
                                        local.get 7
                                        i64.load
                                        i64.const 9
                                        i64.lt_s
                                        br_if 13 (;@5;)
                                        local.get 14
                                        i32.load
                                        i32.const 64
                                        i32.add
                                        i64.const 7
                                        i64.store align=4
                                        local.get 7
                                        i64.load
                                        local.tee 8
                                        i64.const 9
                                        i64.lt_s
                                        br_if 14 (;@4;)
                                        local.get 8
                                        i64.const 9
                                        i64.eq
                                        br_if 15 (;@3;)
                                        local.get 14
                                        i32.load
                                        local.tee 4
                                        i32.const 72
                                        i32.add
                                        local.get 4
                                        i32.const 64
                                        i32.add
                                        i64.load align=4
                                        i64.store align=4
                                        local.get 7
                                        i64.load
                                        i64.const 39
                                        i64.lt_s
                                        br_if 16 (;@2;)
                                        local.get 14
                                        i32.load
                                        i32.const 304
                                        i32.add
                                        i64.const 8
                                        i64.store align=4
                                        local.get 15
                                        i32.load
                                        local.tee 4
                                        i64.load
                                        i64.const 9
                                        i64.lt_s
                                        br_if 12 (;@6;)
                                        local.get 4
                                        i32.const 16
                                        i32.add
                                        i32.load
                                        i32.const 64
                                        i32.add
                                        i64.const 8
                                        i64.store align=4
                                        local.get 15
                                        i32.load
                                        local.tee 4
                                        i64.load
                                        i64.const 10
                                        i64.lt_s
                                        br_if 12 (;@6;)
                                        local.get 4
                                        i32.const 16
                                        i32.add
                                        i32.load
                                        i32.const 72
                                        i32.add
                                        i64.const 8
                                        i64.store align=4
                                        local.get 15
                                        i32.load
                                        local.tee 4
                                        i64.load
                                        i64.const 8
                                        i64.lt_s
                                        br_if 17 (;@1;)
                                        local.get 4
                                        i32.const 16
                                        i32.add
                                        i32.load
                                        i32.const 56
                                        i32.add
                                        local.tee 4
                                        local.get 4
                                        i64.load align=4
                                        i64.const 1
                                        i64.add
                                        i64.store align=4
                                        local.get 7
                                        i64.load
                                        i64.const 9
                                        i64.lt_s
                                        br_if 3 (;@15;)
                                        local.get 16
                                        i32.load
                                        local.tee 4
                                        i64.load
                                        i64.const 9
                                        i64.lt_s
                                        br_if 4 (;@14;)
                                        local.get 4
                                        i32.const 16
                                        i32.add
                                        i32.load
                                        i32.const 64
                                        i32.add
                                        local.get 14
                                        i32.load
                                        i32.const 64
                                        i32.add
                                        i64.load align=4
                                        i64.store align=4
                                        local.get 5
                                        i32.eqz
                                        br_if 5 (;@13;)
                                        local.get 5
                                        i32.load
                                        local.set 18
                                        local.get 5
                                        i64.load offset=8
                                        local.set 8
                                        local.get 5
                                        i64.load offset=16
                                        local.set 19
                                        local.get 5
                                        i64.load offset=24
                                        local.set 20
                                        local.get 5
                                        i64.load offset=32
                                        local.set 21
                                        local.get 5
                                        i32.load offset=40
                                        local.set 22
                                        i64.const 48
                                        call 0
                                        local.tee 4
                                        local.get 22
                                        i32.store offset=40
                                        local.get 4
                                        local.get 21
                                        i64.store offset=32
                                        local.get 4
                                        local.get 20
                                        i64.store offset=24
                                        local.get 4
                                        local.get 19
                                        i64.store offset=16
                                        local.get 4
                                        local.get 8
                                        i64.store offset=8
                                        local.get 4
                                        local.get 18
                                        i32.store
                                        local.get 5
                                        i64.const 5
                                        i64.store offset=24
                                        local.get 5
                                        local.get 4
                                        i32.store
                                        local.get 5
                                        i64.load offset=24
                                        local.set 20
                                        local.get 4
                                        local.get 22
                                        i32.store offset=40
                                        local.get 4
                                        local.get 21
                                        i64.store offset=32
                                        local.get 4
                                        local.get 20
                                        i64.store offset=24
                                        local.get 4
                                        local.get 19
                                        i64.store offset=16
                                        local.get 4
                                        local.get 8
                                        i64.store offset=8
                                        local.get 4
                                        local.get 4
                                        i32.store
                                        local.get 5
                                        i64.const 17
                                        i64.store offset=24
                                        local.get 4
                                        i32.load offset=40
                                        local.set 22
                                        local.get 4
                                        i64.load offset=32
                                        local.set 19
                                        block  ;; label = @19
                                          block  ;; label = @20
                                            local.get 4
                                            i64.load offset=8
                                            local.tee 21
                                            local.get 13
                                            i64.eq
                                            br_if 0 (;@20;)
                                            local.get 5
                                            i32.load
                                            local.set 18
                                            local.get 4
                                            i64.load offset=24
                                            local.set 20
                                            local.get 4
                                            i64.load offset=16
                                            local.set 8
                                            i64.const 48
                                            call 0
                                            local.tee 5
                                            local.get 21
                                            i64.store offset=8
                                            local.get 5
                                            local.get 18
                                            i32.store
                                            local.get 5
                                            local.get 8
                                            i64.store offset=16
                                            local.get 5
                                            local.get 20
                                            i64.store offset=24
                                            local.get 5
                                            local.get 19
                                            i64.store offset=32
                                            local.get 5
                                            local.get 22
                                            i32.store offset=40
                                            br 1 (;@19;)
                                          end
                                          local.get 13
                                          local.set 8
                                          block  ;; label = @20
                                            local.get 5
                                            i64.load offset=16
                                            local.tee 20
                                            local.get 13
                                            i64.eq
                                            br_if 0 (;@20;)
                                            local.get 10
                                            local.get 12
                                            local.get 12
                                            local.get 20
                                            i64.eq
                                            local.tee 18
                                            select
                                            local.set 8
                                            local.get 18
                                            br_if 0 (;@20;)
                                            local.get 11
                                            local.get 20
                                            i64.eq
                                            br_if 0 (;@20;)
                                            local.get 10
                                            local.set 8
                                            local.get 10
                                            local.get 20
                                            i64.eq
                                            br_if 0 (;@20;)
                                            local.get 11
                                            local.get 10
                                            local.get 9
                                            local.get 20
                                            i64.eq
                                            select
                                            local.set 8
                                          end
                                          i64.const 18
                                          local.set 20
                                        end
                                        local.get 4
                                        i32.const 0
                                        i32.store
                                        i32.const 0
                                        local.get 5
                                        i32.store offset=32
                                        local.get 4
                                        local.get 21
                                        i64.store offset=8
                                        local.get 4
                                        local.get 8
                                        i64.store offset=16
                                        local.get 4
                                        local.get 20
                                        i64.store offset=24
                                        local.get 4
                                        local.get 19
                                        i64.store offset=32
                                        local.get 4
                                        local.get 22
                                        i32.store offset=40
                                        local.get 17
                                        i64.const -1
                                        i64.add
                                        local.tee 17
                                        i64.eqz
                                        i32.eqz
                                        br_if 0 (;@18;)
                                      end
                                    end
                                    f64.const 0x0p+0 (;=0;)
                                    local.set 23
                                    block  ;; label = @17
                                      call 7
                                      f64.convert_i64_s
                                      f64.const 0x1.dcd65p+29 (;=1e+09;)
                                      f64.div
                                      local.get 6
                                      f64.convert_i64_s
                                      f64.const 0x1.dcd65p+29 (;=1e+09;)
                                      f64.div
                                      f64.sub
                                      local.get 3
                                      f64.convert_i64_s
                                      f64.const 0x1.dcd65p+29 (;=1e+09;)
                                      f64.div
                                      local.get 2
                                      f64.convert_i64_s
                                      f64.const 0x1.dcd65p+29 (;=1e+09;)
                                      f64.div
                                      f64.sub
                                      f64.sub
                                      local.tee 24
                                      f64.const 0x0p+0 (;=0;)
                                      f64.eq
                                      br_if 0 (;@17;)
                                      local.get 0
                                      f64.convert_i64_s
                                      local.get 24
                                      f64.div
                                      local.set 23
                                    end
                                    i32.const 0
                                    i32.load8_u offset=36
                                    local.set 5
                                    local.get 1
                                    i32.const 0
                                    i32.store8 offset=79
                                    local.get 1
                                    i32.const 56
                                    i32.add
                                    local.get 0
                                    i64.const 0
                                    i32.const 915
                                    local.get 1
                                    i32.const 79
                                    i32.add
                                    call 8
                                    local.get 1
                                    i32.load offset=64
                                    local.set 22
                                    local.get 1
                                    i64.load offset=56
                                    local.set 21
                                    local.get 1
                                    i32.const 0
                                    i32.store8 offset=79
                                    local.get 1
                                    i32.const 40
                                    i32.add
                                    local.get 24
                                    i64.const 0
                                    i32.const 915
                                    local.get 1
                                    i32.const 79
                                    i32.add
                                    call 9
                                    i32.const 1011
                                    i32.const 0
                                    local.get 5
                                    select
                                    local.set 4
                                    i64.const 3
                                    i64.const 0
                                    local.get 5
                                    select
                                    local.set 8
                                    i64.const 4
                                    local.set 19
                                    local.get 1
                                    i32.load offset=48
                                    local.set 15
                                    local.get 1
                                    i64.load offset=40
                                    local.tee 20
                                    i64.const 4
                                    i64.eq
                                    br_if 4 (;@12;)
                                    local.get 20
                                    local.set 19
                                    br 5 (;@11;)
                                  end
                                  local.get 7
                                  i64.load
                                  i64.const 9
                                  i64.lt_s
                                  br_if 10 (;@5;)
                                  local.get 7
                                  i32.const 16
                                  i32.add
                                  i32.load
                                  i32.const 64
                                  i32.add
                                  i64.const 7
                                  i64.store align=4
                                  local.get 7
                                  i64.load
                                  local.tee 8
                                  i64.const 8
                                  i64.le_s
                                  br_if 11 (;@4;)
                                  local.get 8
                                  i64.const 9
                                  i64.eq
                                  br_if 12 (;@3;)
                                  local.get 7
                                  i32.const 16
                                  i32.add
                                  local.tee 5
                                  i32.load
                                  local.tee 4
                                  i32.const 72
                                  i32.add
                                  local.get 4
                                  i32.const 64
                                  i32.add
                                  i64.load align=4
                                  i64.store align=4
                                  local.get 7
                                  i64.load
                                  i64.const 38
                                  i64.le_s
                                  br_if 13 (;@2;)
                                  local.get 5
                                  i32.load
                                  i32.const 304
                                  i32.add
                                  i64.const 8
                                  i64.store align=4
                                  call $.Lstd.internal.types.error.IndexError.__new__:0.305
                                  local.tee 4
                                  i64.const 23
                                  i32.const 848
                                  call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
                                  local.get 4
                                  i64.const 40
                                  i64.store offset=32
                                  local.get 4
                                  i32.const 672
                                  i32.store offset=40
                                  local.get 4
                                  i64.const 67
                                  i64.store offset=48
                                  local.get 4
                                  i32.const 720
                                  i32.store offset=56
                                  local.get 4
                                  i64.const 374
                                  i64.store offset=64
                                  local.get 4
                                  i64.const 13
                                  i64.store offset=72
                                  i32.const 1000
                                  local.get 4
                                  call 4
                                  call 5
                                  unreachable
                                end
                                call $.Lstd.internal.types.error.IndexError.__new__:0.305
                                local.tee 4
                                i64.const 23
                                i32.const 848
                                call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
                                local.get 4
                                i64.const 40
                                i64.store offset=32
                                local.get 4
                                i32.const 672
                                i32.store offset=40
                                local.get 4
                                i64.const 67
                                i64.store offset=48
                                local.get 4
                                i32.const 720
                                i32.store offset=56
                                local.get 4
                                i64.const 374
                                i64.store offset=64
                                local.get 4
                                i64.const 13
                                i64.store offset=72
                                i32.const 1000
                                local.get 4
                                call 4
                                call 5
                                unreachable
                              end
                              call $.Lstd.internal.types.error.IndexError.__new__:0.305
                              local.tee 4
                              i64.const 34
                              i32.const 800
                              call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
                              local.get 4
                              i64.const 40
                              i64.store offset=32
                              local.get 4
                              i32.const 672
                              i32.store offset=40
                              local.get 4
                              i64.const 67
                              i64.store offset=48
                              local.get 4
                              i32.const 720
                              i32.store offset=56
                              local.get 4
                              i64.const 374
                              i64.store offset=64
                              local.get 4
                              i64.const 13
                              i64.store offset=72
                              i32.const 1000
                              local.get 4
                              call 4
                              call 5
                              unreachable
                            end
                            call $.Lstd.internal.types.error.ValueError.__new__:0.526
                            local.tee 4
                            i64.const 16
                            i32.const 528
                            call $.Lstd.internal.types.error.ValueError:std.internal.types.error.ValueError.__init__:3_std.internal.types.error.ValueError_str_.535
                            local.get 4
                            i64.const 36
                            i64.store offset=32
                            local.get 4
                            i32.const 560
                            i32.store offset=40
                            local.get 4
                            i64.const 59
                            i64.store offset=48
                            local.get 4
                            i32.const 608
                            i32.store offset=56
                            local.get 4
                            i64.const 88
                            i64.store offset=64
                            local.get 4
                            i64.const 5
                            i64.store offset=72
                            i32.const 1001
                            local.get 4
                            call 4
                            call 5
                            unreachable
                          end
                          local.get 15
                          i32.load8_u
                          i32.const 45
                          i32.ne
                          br_if 0 (;@11;)
                          local.get 15
                          i32.const 1
                          i32.add
                          i32.load8_u
                          i32.const 110
                          i32.ne
                          br_if 0 (;@11;)
                          local.get 15
                          i32.const 2
                          i32.add
                          i32.load8_u
                          i32.const 97
                          i32.ne
                          br_if 0 (;@11;)
                          i32.const 911
                          local.get 15
                          local.get 15
                          i32.const 3
                          i32.add
                          i32.load8_u
                          i32.const 110
                          i32.eq
                          local.tee 5
                          select
                          local.set 15
                          i64.const 3
                          local.get 20
                          local.get 5
                          select
                          local.set 19
                        end
                        i64.const 8
                        i32.const 916
                        i32.const 0
                        i32.load offset=40
                        local.tee 14
                        call 3
                        i32.const 2
                        local.set 5
                        block  ;; label = @11
                          loop  ;; label = @12
                            local.get 8
                            local.get 4
                            local.get 14
                            call 3
                            local.get 5
                            i32.const 7
                            i32.and
                            local.set 7
                            i64.const 11
                            local.set 8
                            i32.const 925
                            local.set 4
                            i32.const 3
                            local.set 5
                            block  ;; label = @13
                              block  ;; label = @14
                                block  ;; label = @15
                                  local.get 7
                                  i32.const -2
                                  i32.add
                                  br_table 3 (;@12;) 0 (;@15;) 1 (;@14;) 2 (;@13;) 4 (;@11;) 3 (;@12;)
                                end
                                i32.const 4
                                local.set 5
                                local.get 22
                                local.set 4
                                local.get 21
                                local.set 8
                                br 2 (;@12;)
                              end
                              i64.const 10
                              local.set 8
                              i32.const 937
                              local.set 4
                              i32.const 5
                              local.set 5
                              br 1 (;@12;)
                            end
                            i32.const 6
                            local.set 5
                            local.get 15
                            local.set 4
                            local.get 19
                            local.set 8
                            br 0 (;@12;)
                          end
                        end
                        i64.const 1
                        i32.const 1009
                        local.get 14
                        call 3
                        local.get 1
                        i32.const 0
                        i32.store8 offset=79
                        local.get 1
                        i32.const 24
                        i32.add
                        local.get 23
                        i64.const 3
                        i32.const 988
                        local.get 1
                        i32.const 79
                        i32.add
                        call 9
                        local.get 1
                        i32.load offset=32
                        local.set 5
                        local.get 1
                        i64.load offset=24
                        local.set 8
                        local.get 1
                        i32.load8_u offset=79
                        i32.const 1
                        i32.and
                        br_if 2 (;@8;)
                        block  ;; label = @11
                          local.get 8
                          i64.const 4
                          i64.ne
                          br_if 0 (;@11;)
                          local.get 5
                          i32.load8_u
                          i32.const 45
                          i32.ne
                          br_if 0 (;@11;)
                          local.get 5
                          i32.const 1
                          i32.add
                          i32.load8_u
                          i32.const 110
                          i32.ne
                          br_if 0 (;@11;)
                          local.get 5
                          i32.const 2
                          i32.add
                          i32.load8_u
                          i32.const 97
                          i32.ne
                          br_if 0 (;@11;)
                          i32.const 911
                          local.get 5
                          local.get 5
                          i32.const 3
                          i32.add
                          i32.load8_u
                          i32.const 110
                          i32.eq
                          local.tee 4
                          select
                          local.set 5
                          i64.const 3
                          local.get 8
                          local.get 4
                          select
                          local.set 8
                        end
                        i64.const 27
                        i32.const 960
                        i32.const 0
                        i32.load offset=40
                        local.tee 4
                        call 3
                        local.get 8
                        local.get 5
                        local.get 4
                        call 3
                        i64.const 16
                        i32.const 992
                        local.get 4
                        call 3
                        i64.const 1
                        i32.const 1009
                        local.get 4
                        call 3
                        local.get 1
                        i32.const 80
                        i32.add
                        global.set 0
                        return
                      end
                      call $.Lstd.internal.types.error.IndexError.__new__:0.305
                      local.tee 4
                      i64.const 23
                      i32.const 848
                      call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
                      local.get 4
                      i64.const 40
                      i64.store offset=32
                      local.get 4
                      i32.const 672
                      i32.store offset=40
                      local.get 4
                      i64.const 67
                      i64.store offset=48
                      local.get 4
                      i32.const 720
                      i32.store offset=56
                      local.get 4
                      i64.const 374
                      i64.store offset=64
                      local.get 4
                      i64.const 13
                      i64.store offset=72
                      i32.const 1000
                      local.get 4
                      call 4
                      call 5
                      unreachable
                    end
                    call $.Lstd.internal.types.error.IndexError.__new__:0.305
                    local.tee 4
                    i64.const 34
                    i32.const 800
                    call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
                    local.get 4
                    i64.const 40
                    i64.store offset=32
                    local.get 4
                    i32.const 672
                    i32.store offset=40
                    local.get 4
                    i64.const 67
                    i64.store offset=48
                    local.get 4
                    i32.const 720
                    i32.store offset=56
                    local.get 4
                    i64.const 374
                    i64.store offset=64
                    local.get 4
                    i64.const 13
                    i64.store offset=72
                    i32.const 1000
                    local.get 4
                    call 4
                    call 5
                    unreachable
                  end
                  call $.Lstd.internal.types.error.ValueError.__new__:0.526
                  local.set 4
                  local.get 1
                  i32.const 8
                  i32.add
                  i64.const 26
                  i32.const 80
                  local.get 8
                  local.get 5
                  call $.Lstr.cat:0_Tuple_str_str__.662
                  local.get 4
                  local.get 1
                  i64.load offset=8
                  local.get 1
                  i32.load offset=16
                  call $.Lstd.internal.types.error.ValueError:std.internal.types.error.ValueError.__init__:3_std.internal.types.error.ValueError_str_.535
                  local.get 4
                  i64.const 35
                  i64.store offset=32
                  local.get 4
                  i32.const 112
                  i32.store offset=40
                  local.get 4
                  i64.const 51
                  i64.store offset=48
                  local.get 4
                  i32.const 160
                  i32.store offset=56
                  local.get 4
                  i64.const 4
                  i64.store offset=64
                  local.get 4
                  i64.const 2
                  i64.store offset=72
                  i32.const 1001
                  local.get 4
                  call 4
                  call 5
                  unreachable
                end
                local.get 7
                i64.load
                i64.const 9
                i64.lt_s
                br_if 1 (;@5;)
                local.get 7
                i32.const 16
                i32.add
                i32.load
                i32.const 64
                i32.add
                i64.const 7
                i64.store align=4
                local.get 7
                i64.load
                local.tee 8
                i64.const 9
                i64.lt_s
                br_if 2 (;@4;)
                local.get 8
                i64.const 9
                i64.eq
                br_if 3 (;@3;)
                local.get 7
                i32.const 16
                i32.add
                local.tee 14
                i32.load
                local.tee 5
                i32.const 72
                i32.add
                local.get 5
                i32.const 64
                i32.add
                i64.load align=4
                i64.store align=4
                local.get 7
                i64.load
                i64.const 39
                i64.lt_s
                br_if 4 (;@2;)
                local.get 14
                i32.load
                i32.const 304
                i32.add
                i64.const 8
                i64.store align=4
                local.get 4
                i32.const 32
                i32.add
                local.tee 5
                i32.load
                local.tee 7
                i64.load
                i64.const 9
                i64.lt_s
                br_if 0 (;@6;)
                local.get 7
                i32.const 16
                i32.add
                i32.load
                i32.const 64
                i32.add
                i64.const 8
                i64.store align=4
                local.get 5
                i32.load
                local.tee 5
                i64.load
                i64.const 10
                i64.lt_s
                br_if 0 (;@6;)
                local.get 5
                i32.const 16
                i32.add
                i32.load
                i32.const 72
                i32.add
                i64.const 8
                i64.store align=4
                local.get 4
                i32.const 32
                i32.add
                i32.load
                local.tee 4
                i64.load
                i64.const 8
                i64.lt_s
                br_if 5 (;@1;)
                local.get 4
                i32.const 16
                i32.add
                i32.load
                i32.const 56
                i32.add
                local.tee 4
                local.get 4
                i64.load align=4
                i64.const 1
                i64.add
                i64.store align=4
                call $.Lstd.internal.types.error.IndexError.__new__:0.305
                local.tee 4
                i64.const 23
                i32.const 848
                call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
                local.get 4
                i64.const 40
                i64.store offset=32
                local.get 4
                i32.const 672
                i32.store offset=40
                local.get 4
                i64.const 67
                i64.store offset=48
                local.get 4
                i32.const 720
                i32.store offset=56
                local.get 4
                i64.const 374
                i64.store offset=64
                local.get 4
                i64.const 13
                i64.store offset=72
                i32.const 1000
                local.get 4
                call 4
                call 5
                unreachable
              end
              call $.Lstd.internal.types.error.IndexError.__new__:0.305
              local.tee 4
              i64.const 34
              i32.const 800
              call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
              local.get 4
              i64.const 40
              i64.store offset=32
              local.get 4
              i32.const 672
              i32.store offset=40
              local.get 4
              i64.const 67
              i64.store offset=48
              local.get 4
              i32.const 720
              i32.store offset=56
              local.get 4
              i64.const 374
              i64.store offset=64
              local.get 4
              i64.const 13
              i64.store offset=72
              i32.const 1000
              local.get 4
              call 4
              call 5
              unreachable
            end
            call $.Lstd.internal.types.error.IndexError.__new__:0.305
            local.tee 4
            i64.const 34
            i32.const 800
            call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
            local.get 4
            i64.const 40
            i64.store offset=32
            local.get 4
            i32.const 672
            i32.store offset=40
            local.get 4
            i64.const 67
            i64.store offset=48
            local.get 4
            i32.const 720
            i32.store offset=56
            local.get 4
            i64.const 374
            i64.store offset=64
            local.get 4
            i64.const 13
            i64.store offset=72
            i32.const 1000
            local.get 4
            call 4
            call 5
            unreachable
          end
          call $.Lstd.internal.types.error.IndexError.__new__:0.305
          local.tee 4
          i64.const 23
          i32.const 848
          call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
          local.get 4
          i64.const 40
          i64.store offset=32
          local.get 4
          i32.const 672
          i32.store offset=40
          local.get 4
          i64.const 67
          i64.store offset=48
          local.get 4
          i32.const 720
          i32.store offset=56
          local.get 4
          i64.const 374
          i64.store offset=64
          local.get 4
          i64.const 13
          i64.store offset=72
          i32.const 1000
          local.get 4
          call 4
          call 5
          unreachable
        end
        call $.Lstd.internal.types.error.IndexError.__new__:0.305
        local.tee 4
        i64.const 34
        i32.const 800
        call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
        local.get 4
        i64.const 40
        i64.store offset=32
        local.get 4
        i32.const 672
        i32.store offset=40
        local.get 4
        i64.const 67
        i64.store offset=48
        local.get 4
        i32.const 720
        i32.store offset=56
        local.get 4
        i64.const 374
        i64.store offset=64
        local.get 4
        i64.const 13
        i64.store offset=72
        i32.const 1000
        local.get 4
        call 4
        call 5
        unreachable
      end
      call $.Lstd.internal.types.error.IndexError.__new__:0.305
      local.tee 4
      i64.const 34
      i32.const 800
      call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
      local.get 4
      i64.const 40
      i64.store offset=32
      local.get 4
      i32.const 672
      i32.store offset=40
      local.get 4
      i64.const 67
      i64.store offset=48
      local.get 4
      i32.const 720
      i32.store offset=56
      local.get 4
      i64.const 374
      i64.store offset=64
      local.get 4
      i64.const 13
      i64.store offset=72
      i32.const 1000
      local.get 4
      call 4
      call 5
      unreachable
    end
    call $.Lstd.internal.types.error.IndexError.__new__:0.305
    local.tee 4
    i64.const 23
    i32.const 848
    call $.Lstd.internal.types.error.IndexError:std.internal.types.error.IndexError.__init__:5_std.internal.types.error.IndexError_str_.349
    local.get 4
    i64.const 40
    i64.store offset=32
    local.get 4
    i32.const 672
    i32.store offset=40
    local.get 4
    i64.const 67
    i64.store offset=48
    local.get 4
    i32.const 720
    i32.store offset=56
    local.get 4
    i64.const 374
    i64.store offset=64
    local.get 4
    i64.const 13
    i64.store offset=72
    i32.const 1000
    local.get 4
    call 4
    call 5
    unreachable)
  (func $.main.unclash (type 8) (param i32 i32) (result i32)
    (local i32 i64 i32 i32 i32 i64 i64 i64 i64 i64)
    global.get 0
    i32.const 80
    i32.sub
    local.tee 2
    global.set 0
    local.get 0
    i64.extend_i32_u
    local.tee 3
    i64.const 4
    i64.shl
    call 0
    local.set 4
    block  ;; label = @1
      local.get 0
      i32.const 1
      i32.lt_s
      br_if 0 (;@1;)
      local.get 4
      local.set 5
      loop  ;; label = @2
        local.get 1
        i32.load
        local.tee 6
        call 10
        local.set 7
        local.get 5
        local.get 6
        i32.store offset=8
        local.get 5
        local.get 7
        i64.store
        local.get 1
        i32.const 4
        i32.add
        local.set 1
        local.get 5
        i32.const 16
        i32.add
        local.set 5
        local.get 0
        i32.const -1
        i32.add
        local.tee 0
        br_if 0 (;@2;)
      end
    end
    i32.const 0
    local.get 4
    i32.store offset=8
    i32.const 0
    local.get 3
    i64.store
    i32.const 4
    call 11
    i32.const 0
    call 12
    i32.store offset=40
    i32.const 0
    i32.const 1
    i32.store8 offset=36
    i32.const 0
    i32.const 1
    i32.store8 offset=24
    i32.const 0
    i32.const 1
    i32.store8 offset=25
    i32.const 0
    i32.const 1
    i32.store8 offset=26
    i32.const 0
    i32.const 1
    i32.store8 offset=27
    i32.const 0
    i32.const 1
    i32.store8 offset=28
    i32.const 0
    i32.const 0
    i32.store8 offset=16
    i64.const 24
    call 0
    local.tee 1
    i32.const 16
    i32.add
    local.tee 6
    i64.const 408
    call 1
    local.tee 5
    i32.store
    local.get 1
    i64.const 51
    i64.store offset=8
    block  ;; label = @1
      i32.const 1
      br_if 0 (;@1;)
      local.get 6
      local.get 5
      i64.const 8
      i64.const 0
      call 6
      local.tee 5
      i32.store
      local.get 1
      i64.const 1
      i64.store offset=8
    end
    local.get 5
    i64.const 0
    i64.store align=4
    i64.const 4
    local.set 3
    i64.const 2
    local.set 7
    i32.const 8
    local.set 5
    i64.const 8
    local.set 8
    loop  ;; label = @1
      local.get 1
      i32.load offset=16
      local.set 0
      block  ;; label = @2
        local.get 7
        i64.const -1
        i64.add
        local.get 1
        i64.load offset=8
        i64.ne
        br_if 0 (;@2;)
        local.get 6
        local.get 0
        local.get 3
        i64.const 1
        i64.shr_u
        local.tee 9
        i64.const 3
        i64.shl
        local.get 8
        call 6
        local.tee 0
        i32.store
        local.get 1
        local.get 9
        i64.store offset=8
      end
      local.get 0
      local.get 5
      i32.add
      i64.const 0
      i64.store align=4
      local.get 1
      local.get 7
      i64.store
      local.get 5
      i32.const 8
      i32.add
      local.set 5
      local.get 7
      i64.const 1
      i64.add
      local.set 7
      local.get 8
      i64.const 8
      i64.add
      local.set 8
      local.get 3
      i64.const 3
      i64.add
      local.tee 3
      i64.const 154
      i64.ne
      br_if 0 (;@1;)
    end
    i32.const 0
    local.set 0
    i32.const 0
    local.get 1
    i32.store offset=20
    i64.const 0
    local.set 7
    i64.const 204
    call 0
    local.set 5
    i64.const 51
    local.set 3
    loop  ;; label = @1
      block  ;; label = @2
        local.get 7
        local.get 3
        i64.ne
        br_if 0 (;@2;)
        local.get 5
        local.get 3
        i64.const 3
        i64.mul
        i64.const 1
        i64.add
        local.tee 8
        i64.const 2
        i64.div_s
        i64.const 1
        local.get 8
        i64.const 1
        i64.gt_s
        select
        local.tee 8
        i64.const 2
        i64.shl
        local.get 3
        i64.const 2
        i64.shl
        call 6
        local.set 5
        local.get 8
        local.set 3
      end
      local.get 5
      local.get 0
      i32.add
      local.get 1
      i32.store
      local.get 0
      i32.const 4
      i32.add
      local.set 0
      local.get 7
      i64.const 1
      i64.add
      local.tee 7
      i64.const 51
      i64.ne
      br_if 0 (;@1;)
    end
    i32.const 0
    i64.const 204
    call 0
    local.tee 0
    i32.store offset=1120
    i64.const 51
    local.set 9
    i32.const 0
    i64.const 51
    i64.store offset=1112
    i32.const 0
    i64.const 0
    i64.store offset=1104
    i64.const 51
    local.set 8
    i64.const 0
    local.set 7
    loop  ;; label = @1
      local.get 5
      i32.load
      local.tee 1
      i32.const 16
      i32.add
      i32.load
      local.set 6
      local.get 1
      i32.const 8
      i32.add
      i64.load
      local.set 3
      local.get 1
      i64.load
      local.set 10
      i64.const 24
      call 0
      local.set 1
      local.get 3
      i64.const 3
      i64.shl
      local.tee 11
      call 1
      local.get 6
      local.get 11
      i32.wrap_i64
      call 2
      local.set 6
      local.get 1
      local.get 3
      i64.store offset=8
      local.get 1
      local.get 10
      i64.store
      local.get 1
      i32.const 16
      i32.add
      local.get 6
      i32.store
      block  ;; label = @2
        block  ;; label = @3
          local.get 8
          local.get 7
          i64.eq
          br_if 0 (;@3;)
          local.get 7
          local.set 3
          br 1 (;@2;)
        end
        i32.const 0
        local.get 0
        local.get 8
        i64.const 3
        i64.mul
        i64.const 1
        i64.add
        local.tee 3
        i64.const 2
        i64.div_s
        i64.const 1
        local.get 3
        i64.const 1
        i64.gt_s
        select
        local.tee 10
        i64.const 2
        i64.shl
        local.get 8
        i64.const 2
        i64.shl
        call 6
        local.tee 0
        i32.store offset=1120
        i32.const 0
        local.get 10
        i64.store offset=1112
        i32.const 0
        i64.load offset=1104
        local.set 3
        local.get 10
        local.set 8
      end
      local.get 0
      local.get 7
      i32.wrap_i64
      i32.const 2
      i32.shl
      i32.add
      local.get 1
      i32.store
      i32.const 0
      local.get 3
      i64.const 1
      i64.add
      local.tee 7
      i64.store offset=1104
      local.get 5
      i32.const 4
      i32.add
      local.set 5
      local.get 9
      i64.const -1
      i64.add
      local.tee 9
      i64.const 0
      i64.ne
      br_if 0 (;@1;)
    end
    i32.const 0
    i32.const 0
    i32.store offset=32
    block  ;; label = @1
      i32.const 0
      i32.load8_u offset=16
      br_if 0 (;@1;)
      i32.const 0
      i32.const 1
      i32.store8 offset=16
      i32.const 0
      i32.const 0
      i64.load
      local.tee 7
      i64.store offset=1144
      i32.const 0
      local.get 7
      i64.store offset=1136
      i32.const 0
      i32.const 0
      i32.load offset=8
      i32.store offset=1152
      call 13
      drop
      call 12
      drop
      i32.const 0
      call 14
      i32.store offset=1160
    end
    block  ;; label = @1
      block  ;; label = @2
        i32.const 0
        i64.load offset=1136
        i64.const -1
        i64.add
        local.tee 3
        i64.const 2
        i64.ge_s
        br_if 0 (;@2;)
        i64.const 50000
        local.set 7
        block  ;; label = @3
          local.get 3
          i64.const 1
          i64.ne
          br_if 0 (;@3;)
          i32.const 0
          i32.load offset=1152
          local.tee 5
          i32.const 24
          i32.add
          i32.load
          local.set 0
          i64.const 0
          local.set 3
          block  ;; label = @4
            local.get 5
            i32.const 16
            i32.add
            i64.load
            local.tee 8
            i64.const 1
            i64.lt_s
            br_if 0 (;@4;)
            i64.const 0
            local.set 3
            local.get 0
            local.set 5
            loop  ;; label = @5
              local.get 5
              i32.load8_u
              call 15
              i32.eqz
              br_if 1 (;@4;)
              local.get 5
              i32.const 1
              i32.add
              local.set 5
              local.get 8
              local.get 3
              i64.const 1
              i64.add
              local.tee 3
              i64.ne
              br_if 0 (;@5;)
            end
            local.get 8
            local.set 3
          end
          local.get 8
          local.get 3
          i64.sub
          local.set 7
          local.get 0
          local.get 8
          i32.wrap_i64
          i32.add
          local.set 5
          local.get 0
          local.get 3
          i32.wrap_i64
          i32.add
          local.set 6
          block  ;; label = @4
            loop  ;; label = @5
              local.get 5
              local.set 1
              local.get 7
              local.tee 3
              i64.const -1
              i64.add
              local.tee 7
              i64.const 0
              i64.lt_s
              br_if 1 (;@4;)
              local.get 1
              i32.const -1
              i32.add
              local.tee 5
              i32.load8_u
              call 15
              br_if 0 (;@5;)
            end
          end
          local.get 2
          i32.const 0
          i32.store offset=72
          local.get 3
          local.get 6
          local.get 2
          i32.const 72
          i32.add
          i32.const 10
          call 16
          local.set 7
          local.get 3
          i64.const 0
          i64.eq
          br_if 2 (;@1;)
          local.get 1
          local.get 2
          i32.load offset=72
          i32.ne
          br_if 2 (;@1;)
        end
        local.get 7
        call $main
        local.get 2
        i32.const 80
        i32.add
        global.set 0
        i32.const 0
        return
      end
      local.get 2
      i32.const 0
      i32.store8 offset=72
      local.get 2
      i32.const 56
      i32.add
      local.get 3
      i64.const 0
      i32.const 915
      local.get 2
      i32.const 72
      i32.add
      call 8
      local.get 2
      i32.load offset=64
      local.set 5
      local.get 2
      i64.load offset=56
      local.tee 7
      i64.const 24
      i64.add
      local.tee 3
      call 1
      local.get 5
      local.get 7
      i32.wrap_i64
      local.tee 1
      call 2
      local.tee 0
      local.get 1
      i32.add
      local.tee 5
      i32.const 16
      i32.add
      i32.const 0
      i64.load offset=1040
      i64.store align=1
      local.get 5
      i32.const 8
      i32.add
      i32.const 0
      i64.load offset=1032
      i64.store align=1
      local.get 5
      i32.const 0
      i64.load offset=1024
      i64.store align=1
      local.get 3
      local.get 0
      call $.Lerror.5:0_str_.754
      unreachable
    end
    i64.const 88
    call 0
    local.set 5
    local.get 2
    i32.const 0
    i32.store8 offset=79
    local.get 2
    i32.const 40
    i32.add
    i64.const 10
    i64.const 0
    i32.const 915
    local.get 2
    i32.const 79
    i32.add
    call 8
    local.get 2
    i32.load offset=48
    local.set 1
    local.get 2
    i64.load offset=40
    local.set 7
    local.get 2
    i32.const 24
    i32.add
    local.get 8
    local.get 0
    call $.Lstr:str.__repr__:0_str_.821
    local.get 2
    i32.const 8
    i32.add
    i64.const 36
    i32.const 384
    local.get 7
    local.get 1
    i64.const 2
    i32.const 421
    local.get 2
    i64.load offset=24
    local.get 2
    i32.load offset=32
    call $.Lstr.cat:0_Tuple_str_str_str_str__.839
    local.get 2
    i64.load offset=8
    local.set 7
    local.get 2
    i32.load offset=16
    local.set 1
    local.get 5
    i32.const 0
    i32.store offset=80
    local.get 5
    i64.const 13
    i64.store offset=72
    local.get 5
    i64.const 407
    i64.store offset=64
    local.get 5
    i32.const 464
    i32.store offset=56
    local.get 5
    i64.const 52
    i64.store offset=48
    local.get 5
    i32.const 432
    i32.store offset=40
    local.get 5
    i64.const 29
    i64.store offset=32
    local.get 5
    local.get 1
    i32.store offset=24
    local.get 5
    local.get 7
    i64.store offset=16
    local.get 5
    i32.const 55
    i32.store offset=8
    local.get 5
    i64.const 10
    i64.store
    i32.const 1001
    local.get 5
    call 4
    call 5
    unreachable)
  (export "main" (func $main))
  (memory (;0;) 2)
  (data $.L..argv (i32.const 0) "\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00")
  (data $.._import_sys_232.0 (i32.const 16) "\00")
  (data $.L.Array1Glob (i32.const 20) "\00\00\00\00")
  (data $.Ident1 (i32.const 24) "\00")
  (data $.Ident2 (i32.const 25) "\00")
  (data $.Ident3 (i32.const 26) "\00")
  (data $.Ident4 (i32.const 27) "\00")
  (data $.Ident5 (i32.const 28) "\00")
  (data $.L.PtrGlb (i32.const 32) "\00\00\00\00")
  (data $.__version__ (i32.const 36) "\00")
  (data $.L._stdout (i32.const 40) "\00\00\00\00")
  (data $.L.str.2 (i32.const 44) "IndexError\00")
  (data $.L.str.6 (i32.const 55) "ValueError\00")
  (data $.L.str.18 (i32.const 80) "invalid format specifier: \00")
  (data $.L.str.19 (i32.const 112) "std.internal.format._format_error:0\00")
  (data $.L.str.20 (i32.const 160) "/root/.codon/lib/codon/stdlib/internal/format.codon\00")
  (data $.L.str.29 (i32.const 212) "SystemExit\00")
  (data $.L.str.31 (i32.const 223) "std.sys.exit:0\00")
  (data $.L.str.32 (i32.const 240) "/root/.codon/lib/codon/stdlib/sys.codon\00")
  (data $.L.str.34 (i32.const 280) " \00")
  (data $.L.str.35 (i32.const 282) "usage: \00")
  (data $.L.str.36 (i32.const 304) " [number_of_loops]\00")
  (data $.L.str.42 (i32.const 323) "'\00")
  (data $.L.str.43 (i32.const 325) "\5c'\00")
  (data $.L.str.46 (i32.const 328) "\22\00")
  (data $.L.str.47 (i32.const 330) "\5c\22\00")
  (data $.L.str.49 (i32.const 333) "\5cn\00")
  (data $.L.str.51 (i32.const 336) "\5cr\00")
  (data $.L.str.53 (i32.const 339) "\5ct\00")
  (data $.L.str.55 (i32.const 342) "\5c\5c\00")
  (data $.L.str.56 (i32.const 352) "0123456789abcdef\00")
  (data $.L.str.63 (i32.const 384) "invalid literal for int() with base \00")
  (data $.L.str.64 (i32.const 421) ": \00")
  (data $.L.str.65 (i32.const 432) "int._from_str:0.parse_error:0\00")
  (data $.L.str.66 (i32.const 464) "/root/.codon/lib/codon/stdlib/internal/builtin.codon\00")
  (data $.L.str.78 (i32.const 528) "optional is None\00")
  (data $.L.str.79 (i32.const 560) "std.internal.types.optional.unwrap:0\00")
  (data $.L.str.80 (i32.const 608) "/root/.codon/lib/codon/stdlib/internal/types/optional.codon\00")
  (data $.L.str.84 (i32.const 672) "std.internal.types.ptr.List._idx_check:0\00")
  (data $.L.str.85 (i32.const 720) "/root/.codon/lib/codon/stdlib/internal/types/collections/list.codon\00")
  (data $.L.str.86 (i32.const 800) "list assignment index out of range\00")
  (data $.L.str.94 (i32.const 848) "list index out of range\00")
  (data $.L.str.104 (i32.const 880) "DHRYSTONE PROGRAM, SOME STRING\00")
  (data $.L.str.112 (i32.const 911) "nan\00")
  (data $.L.str.113 (i32.const 915) "\00")
  (data $.L.str.120 (i32.const 916) "Pystone(\00")
  (data $.L.str.121 (i32.const 925) ") time for \00")
  (data $.L.str.122 (i32.const 937) " passes = \00")
  (data $.L.str.125 (i32.const 960) "This machine benchmarks at \00")
  (data $.L.str.126 (i32.const 988) ".1f\00")
  (data $.L.str.127 (i32.const 992) " pystones/second\00")
  (data $.L.str.129 (i32.const 1009) "\0a\00")
  (data $.L.str.134 (i32.const 1011) "1.1\00")
  (data $.L.str.138 (i32.const 1024) " arguments are too many;\00")
  (data (;54;) (i32.const 1056) "\e9\03\00\00")
  (data (;55;) (i32.const 1072) "Invalid argument \00")
  (data (;56;) (i32.const 1096) "\00\00\00\00")
  (data $.Array2Glob.body (i32.const 1104) "\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00")
  (data $.argv.body (i32.const 1136) "\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00\00")
  (data $.stderr.1.body.2 (i32.const 1160) "\00\00\00\00"))
