(module
  (memory (export "memory") 1)
  (data (i32.const 100) "fine")
  ;; Return area for `greet`: disc=0 (Ok), padding, (strPtr=100, strLen=4).
  (data (i32.const 200) "\00\00\00\00\64\00\00\00\04\00\00\00")
  ;; Return area for `fail`: disc=1 (Err), padding, errCode=404 at +4.
  (data (i32.const 220) "\01\00\00\00\94\01\00\00\00\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "greet") (result i32) i32.const 200)
  (func (export "fail")  (result i32) i32.const 220))
