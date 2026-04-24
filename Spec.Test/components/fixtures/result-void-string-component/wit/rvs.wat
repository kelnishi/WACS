(module
  (memory (export "memory") 1)
  (data (i32.const 100) "permission denied")
  ;; Return area for `ok`: disc=0 (Ok — void, nothing to read).
  (data (i32.const 200) "\00\00\00\00\00\00\00\00\00\00\00\00")
  ;; Return area for `err`: disc=1 (Err), padding, (strPtr=100, strLen=17).
  (data (i32.const 220) "\01\00\00\00\64\00\00\00\11\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "ok")  (result i32) i32.const 200)
  (func (export "err") (result i32) i32.const 220))
