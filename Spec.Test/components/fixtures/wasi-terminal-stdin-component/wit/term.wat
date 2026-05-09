(module
  (import "wasi:cli/terminal-stdin@0.2.8" "get-terminal-stdin"
    (func $get_term (param i32)))
  (import "wasi:cli/terminal-input@0.2.8" "[resource-drop]terminal-input"
    (func $drop (param i32)))
  (memory (export "memory") 1)
  (func $realloc (param i32 i32 i32 i32) (result i32) i32.const 1024)
  (export "cabi_realloc" (func $realloc))
  ;; check() -> u32: read disc + handle from retArea, drop
  ;; if Some, return disc as u32 (0=None, 1=Some).
  (func (export "check") (result i32)
    (local $r i32)
    (local $disc i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 4) (i32.const 8)))
    (call $get_term (local.get $r))
    (local.set $disc (i32.load8_u (local.get $r)))
    (if (i32.eq (local.get $disc) (i32.const 1))
      (then (call $drop (i32.load offset=4 (local.get $r)))))
    (local.get $disc)))
