(module
  (memory (export "memory") 1)
  (data (i32.const 100) "greetings")
  ;; Return area at 200: (disc: u8, _pad[3], strPtr: i32, strLen: i32)
  ;; pre-populated for the Some branch.
  (data (i32.const 200) "\01\00\00\00\64\00\00\00\09\00\00\00")
  ;; Return area at 220: disc=0 (None).
  (data (i32.const 220) "\00\00\00\00\00\00\00\00\00\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "maybe-some") (result i32) i32.const 200)
  (func (export "maybe-none") (result i32) i32.const 220))
