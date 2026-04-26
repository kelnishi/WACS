(module
  (memory (export "memory") 1)
  ;; Latin-1 buffer at 100: "Hi" = 0x48 0x69, 2 bytes.
  (data (i32.const 100) "\48\69")
  ;; UTF-16LE buffer at 200: "Hi" = 0x48 0x00 0x69 0x00, 4 bytes
  ;; (2 code units).
  (data (i32.const 200) "\48\00\69\00")
  ;; latin retArea at 400: (strPtr=100, taggedLen=2). High bit
  ;; clear → Latin-1 decode; len is byte count.
  (data (i32.const 400) "\64\00\00\00\02\00\00\00")
  ;; wide retArea at 408: (strPtr=200, taggedLen=2 | 0x80000000).
  ;; High bit set → UTF-16 decode; len & ~tag is u16 code units.
  ;; Little-endian: 0x80000002 = 02 00 00 80.
  (data (i32.const 408) "\C8\00\00\00\02\00\00\80")
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 600)
  (export "cabi_realloc" (func $realloc))
  (func (export "latin") (result i32) i32.const 400)
  (func (export "wide")  (result i32) i32.const 408))
