;; Classic iterative fib benchmark — tight hot loop with i32 arithmetic,
;; branches, and local accesses. Good for measuring pure dispatch throughput
;; without touching memory/tables/imports.
(module
  (func $fib-iter (export "fib") (param $n i32) (result i32)
    (local $a i32) (local $b i32) (local $tmp i32) (local $i i32)
    (local.set $a (i32.const 0))
    (local.set $b (i32.const 1))
    (local.set $i (i32.const 0))
    (block $end
      (loop $top
        (br_if $end (i32.ge_s (local.get $i) (local.get $n)))
        (local.set $tmp (i32.add (local.get $a) (local.get $b)))
        (local.set $a (local.get $b))
        (local.set $b (local.get $tmp))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $top)
      )
    )
    (local.get $a)
  )

  ;; Recursive fib — exponential, good for dispatch+call overhead.
  (func $fib-rec (export "fib-rec") (param $n i32) (result i32)
    (if (result i32) (i32.lt_s (local.get $n) (i32.const 2))
      (then (local.get $n))
      (else
        (i32.add
          (call $fib-rec (i32.sub (local.get $n) (i32.const 1)))
          (call $fib-rec (i32.sub (local.get $n) (i32.const 2)))
        )
      )
    )
  )

  ;; Iterative factorial — mul-heavy inner loop.
  (func $fac-iter (export "fac") (param $n i32) (result i64)
    (local $acc i64) (local $i i32)
    (local.set $acc (i64.const 1))
    (local.set $i (i32.const 2))
    (block $end
      (loop $top
        (br_if $end (i32.gt_s (local.get $i) (local.get $n)))
        (local.set $acc (i64.mul (local.get $acc) (i64.extend_i32_s (local.get $i))))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $top)
      )
    )
    (local.get $acc)
  )

  ;; Tight integer loop — just addition, no branches other than loop back.
  (func $sum (export "sum") (param $n i32) (result i64)
    (local $acc i64) (local $i i32)
    (local.set $acc (i64.const 0))
    (local.set $i (i32.const 0))
    (block $end
      (loop $top
        (br_if $end (i32.ge_s (local.get $i) (local.get $n)))
        (local.set $acc (i64.add (local.get $acc) (i64.extend_i32_s (local.get $i))))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $top)
      )
    )
    (local.get $acc)
  )
)
