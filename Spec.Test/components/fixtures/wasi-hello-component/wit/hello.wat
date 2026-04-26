(module
  (import "wasi:cli/stdout@0.2.3" "get-stdout"
    (func $get_stdout (result i32)))
  (import "wasi:io/streams@0.2.3" "[method]output-stream.blocking-write-and-flush"
    (func $write (param i32 i32 i32 i32)))
  (import "wasi:io/streams@0.2.3" "[resource-drop]output-stream"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (data (i32.const 200) "hello\n")
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  (func (export "greet")
    (local $stdout i32)
    (local $r i32)
    (local.set $stdout (call $get_stdout))
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 16)))
    (call $write (local.get $stdout) (i32.const 200) (i32.const 6) (local.get $r))
    (call $drop (local.get $stdout))))
