(module
  (memory (export "memory") 1)
  ;; Three UTF-8 strings laid out at 100, 110, 120.
  (data (i32.const 100) "alpha")
  (data (i32.const 110) "beta")
  (data (i32.const 120) "gamma")
  ;; Element array at 200 — 3 elements, each 8 bytes:
  ;; (strPtr=100,strLen=5) (strPtr=110,strLen=4) (strPtr=120,strLen=5).
  (data (i32.const 200)
    "\64\00\00\00\05\00\00\00"
    "\6E\00\00\00\04\00\00\00"
    "\78\00\00\00\05\00\00\00")
  ;; Return area at 300: (listPtr=200, count=3).
  (data (i32.const 300) "\C8\00\00\00\03\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 400)
  (export "cabi_realloc" (func $realloc))
  (func (export "words") (result i32) i32.const 300))
