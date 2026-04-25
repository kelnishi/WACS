(module
  (memory (export "memory") 1)
  ;; Three u32 elements at addr 100: [10, 20, 30].
  (data (i32.const 100) "\0A\00\00\00\14\00\00\00\1E\00\00\00")
  ;; retArea at 200: disc=1 ("found" case), padding,
  ;; (dataPtr=100, count=3) at offset 4/8.
  (data (i32.const 200) "\01\00\00\00\64\00\00\00\03\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "discover") (result i32) i32.const 200))
