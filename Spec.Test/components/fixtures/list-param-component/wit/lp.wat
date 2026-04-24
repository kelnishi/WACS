(module
  (memory (export "memory") 1)
  ;; Bump allocator — first call is (0, 0, align, newLen).
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
  ;; sum: `(xs: list<u32>) -> u32`. Core signature lowers to
  ;; `(ptr: i32, count: i32) -> i32`. Iterate ptr..ptr+count*4
  ;; in 4-byte strides and accumulate.
  (func (export "sum") (param $ptr i32) (param $count i32) (result i32)
    (local $acc i32)
    (local $end i32)
    i32.const 0
    local.set $acc
    local.get $ptr
    local.get $count
    i32.const 4
    i32.mul
    i32.add
    local.set $end
    (block $done
      (loop $lp
        local.get $ptr
        local.get $end
        i32.ge_u
        br_if $done
        local.get $acc
        local.get $ptr
        i32.load
        i32.add
        local.set $acc
        local.get $ptr
        i32.const 4
        i32.add
        local.set $ptr
        br $lp))
    local.get $acc))
