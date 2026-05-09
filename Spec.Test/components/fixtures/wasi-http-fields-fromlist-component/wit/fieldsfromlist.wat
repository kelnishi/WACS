(module
  (import "wasi:http/types@0.2.8" "[static]fields.from-list"
    (func $fromList (param i32 i32 i32)))
  (memory (export "memory") 1)
  ;; Pre-laid-out data:
  ;; offset 16: "content-type" (12 bytes)
  ;; offset 32: "text/plain" (10 bytes)
  ;; offset 48: "authorization" (13 bytes)
  ;; offset 64: "Bearer XYZ" (10 bytes)
  (data (i32.const 16) "content-type")
  (data (i32.const 32) "text/plain")
  (data (i32.const 48) "authorization")
  (data (i32.const 64) "Bearer XYZ")
  ;; List array at offset 128: 2 entries × 16 bytes = 32 bytes
  ;; Entry 0 (offset 128): keyPtr=16, keyLen=12, valPtr=32, valLen=10
  (data (i32.const 128) "\10\00\00\00\0c\00\00\00\20\00\00\00\0a\00\00\00")
  ;; Entry 1 (offset 144): keyPtr=48, keyLen=13, valPtr=64, valLen=10
  (data (i32.const 144) "\30\00\00\00\0d\00\00\00\40\00\00\00\0a\00\00\00")
  (global $next (mut i32) (i32.const 1024))
  (func $realloc (param i32 i32 i32 i32) (result i32)
    (local $r i32) (local $align i32)
    (local.set $align (local.get 2))
    (global.set $next
      (i32.and
        (i32.add (global.get $next) (i32.sub (local.get $align) (i32.const 1)))
        (i32.xor (i32.const -1) (i32.sub (local.get $align) (i32.const 1)))))
    (local.set $r (global.get $next))
    (global.set $next
      (i32.add (global.get $next) (local.get 3)))
    (local.get $r))
  (export "cabi_realloc" (func $realloc))
  (func (export "ask-from-list") (result i32)
    (local $r i32)
    ;; result<own<fields>, header-error>: 8 bytes (disc + 3 pad + handle)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    ;; from-list(listPtr=128, listLen=2, retArea=$r)
    (call $fromList (i32.const 128) (i32.const 2) (local.get $r))
    ;; Return the allocated handle from retArea+4
    (i32.load offset=4 (local.get $r))))
