(module
  (import "wasi:cli/exit@0.2.3" "exit"
    (func $exit (param i32)))
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  ;; call-exit-ok: pass discriminator 0 = Ok.
  (func (export "call-exit-ok")
    (call $exit (i32.const 0)))
  ;; call-exit-err: pass discriminator 1 = Err.
  (func (export "call-exit-err")
    (call $exit (i32.const 1))))
