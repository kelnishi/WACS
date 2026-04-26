(module
  (import "local:res/things" "[method]thing.get-value"
    (func $get_val (param i32) (result i32)))
  (import "local:res/things" "make"
    (func $make (result i32)))
  (import "local:res/things" "[resource-drop]thing"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  ;; trip: make a thing, get its value, drop it, return value.
  (func (export "trip") (result i32)
    (local $h i32)
    (local $v i32)
    (local.set $h (call $make))
    (local.set $v (call $get_val (local.get $h)))
    (call $drop (local.get $h))
    (local.get $v)))
