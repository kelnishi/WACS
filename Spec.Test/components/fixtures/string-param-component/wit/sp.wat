(module
  (memory (export "memory") 1)
  ;; Bump allocator tracked by a mutable global. cabi_realloc's
  ;; first call is (0, 0, align, newLen) so we only handle that
  ;; branch for v0.
  (global $tip (mut i32) (i32.const 1024))
  (func $realloc (param $oldPtr i32) (param $oldLen i32)
                 (param $align i32) (param $newLen i32)
                 (result i32)
    (local $ret i32)
    global.get $tip
    local.set $ret
    global.get $tip
    local.get $newLen
    i32.add
    global.set $tip
    local.get $ret)
  (export "cabi_realloc" (func $realloc))
  ;; echo: `(s: string) -> string`. Core signature lowers to
  ;; (ptr, len) -> i32 where the returned i32 points at a
  ;; 2-word return area. Write (ptr, len) there verbatim —
  ;; we're echoing the already-allocated input bytes.
  (func (export "echo") (param $ptr i32) (param $len i32) (result i32)
    i32.const 100
    local.get $ptr
    i32.store
    i32.const 104
    local.get $len
    i32.store
    i32.const 100))
