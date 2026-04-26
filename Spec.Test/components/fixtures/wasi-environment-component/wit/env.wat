(module
  (import "wasi:cli/environment@0.2.3" "get-arguments"
    (func $get_args (param i32)))
  (import "wasi:cli/environment@0.2.3" "initial-cwd"
    (func $get_cwd (param i32)))
  (import "wasi:cli/environment@0.2.3" "get-environment"
    (func $get_env (param i32)))
  (memory (export "memory") 1)
  (global $next (mut i32) (i32.const 1024))
  (func $realloc (param i32 i32 i32 i32) (result i32)
    (local $r i32)
    (local $align i32)
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
  (func (export "get-args") (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $get_args (local.get $r))
    (local.get $r))
  (func (export "get-cwd") (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 12)))
    (call $get_cwd (local.get $r))
    (local.get $r))
  (func (export "get-env") (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $get_env (local.get $r))
    (local.get $r)))
