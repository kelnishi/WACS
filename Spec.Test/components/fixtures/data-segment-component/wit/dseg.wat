;; Active data segment + string-return — exercises every gap-14
;; host path:
;;   - active-segment install at module ctor (BulkHelpers.CopySegmentToMemory)
;;   - string lift from guest memory (StringMarshal.LiftUtf8 reading a span)
;;   - retArea i32-pair read (PrimitiveStore.ReadI32LE)
;;
;; "hello world" is laid down at offset 100 (active data segment);
;; the retArea at 200 holds (100, 11) for the lift to read.
(module
  (memory (export "memory") 1)
  (data (i32.const 100) "hello world")
  (data (i32.const 200) "\64\00\00\00\0b\00\00\00")  ;; ptr=100, len=11
  (func $realloc (param i32 i32 i32 i32) (result i32)
    i32.const 300)
  (export "cabi_realloc" (func $realloc))
  (func (export "greet") (result i32)
    i32.const 200))
