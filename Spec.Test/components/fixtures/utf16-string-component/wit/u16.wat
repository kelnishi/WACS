(module
  (memory (export "memory") 1)
  ;; "Hi" as UTF-16LE: H=0x48, i=0x69 — 4 bytes / 2 code units.
  (data (i32.const 100) "\48\00\69\00")
  ;; retArea at 200: (strPtr=100, strLen=2 code units).
  ;; Note: for UTF-16, len is u16 code units, not bytes.
  (data (i32.const 200) "\64\00\00\00\02\00\00\00")
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "greet") (result i32) i32.const 200))
