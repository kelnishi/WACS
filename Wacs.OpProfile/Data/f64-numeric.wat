;; f64-numeric.wat — synthetic f64-heavy workload.
;;
;; Two numeric kernels that approximate what shows up inside numerical
;; libraries (Spectra, Eigen, a physics solver, an image filter):
;;
;;  * matmul: dense N×N × N×N matrix multiply in a stride-1 triple loop. The
;;    innermost body is exactly one f64.mul + one f64.add per iteration,
;;    with surrounding i32 loop arithmetic and linear-memory f64 loads/stores.
;;    This mirrors the arithmetic-to-dispatch ratio inside Eigen's core
;;    `lazyProduct` kernel before any SIMD lowering.
;;
;;  * reduce: sum-of-products over a flat f64 array — the body of a dot
;;    product / norm-squared. Single pass, no nesting; exercises f64 load +
;;    mul + add with minimal loop overhead.
;;
;; Memory layout:
;;   [0, N*N*8)           = A  (rows of N f64s, stride = N*8 bytes)
;;   [N*N*8, 2*N*N*8)     = B
;;   [2*N*N*8, 3*N*N*8)   = C  (matmul output)
;;
;; The outer driver (_start) calls matmul(N) twice and reduce once so both
;; kernels appear in the opcode histogram with comparable weight.

(module
  (memory (export "memory") 16)   ;; 16 * 64KiB = 1MiB — room for three 180×180 matrices.

  ;; Fill [base, base + count*8) with a deterministic f64 sequence so the
  ;; result depends on real reads (prevents the JIT from const-folding).
  (func $fill (param $base i32) (param $count i32)
    (local $i i32) (local $p i32)
    (local.set $p (local.get $base))
    (local.set $i (i32.const 0))
    (block $done
      (loop $top
        (br_if $done (i32.ge_s (local.get $i) (local.get $count)))
        (f64.store (local.get $p)
          (f64.add (f64.convert_i32_s (local.get $i)) (f64.const 1.5)))
        (local.set $p (i32.add (local.get $p) (i32.const 8)))
        (local.set $i (i32.add (local.get $i) (i32.const 1)))
        (br $top)
      )
    )
  )

  ;; C[i,j] = sum_k A[i,k] * B[k,j]  for an N×N matrix stored row-major as f64.
  (func $matmul (export "matmul") (param $N i32)
    (local $i i32) (local $j i32) (local $k i32)
    (local $rowA i32) (local $colB i32)
    (local $acc f64) (local $aik f64) (local $bkj f64)
    (local $stride i32) (local $Abase i32) (local $Bbase i32) (local $Cbase i32)
    (local.set $stride (i32.mul (local.get $N) (i32.const 8)))
    (local.set $Abase (i32.const 0))
    (local.set $Bbase (i32.mul (local.get $N) (i32.mul (local.get $N) (i32.const 8))))
    (local.set $Cbase (i32.mul (i32.const 2) (local.get $Bbase)))

    (local.set $i (i32.const 0))
    (block $i_done (loop $i_top
      (br_if $i_done (i32.ge_s (local.get $i) (local.get $N)))
      (local.set $j (i32.const 0))
      (block $j_done (loop $j_top
        (br_if $j_done (i32.ge_s (local.get $j) (local.get $N)))
        (local.set $acc (f64.const 0))
        (local.set $k (i32.const 0))
        (block $k_done (loop $k_top
          (br_if $k_done (i32.ge_s (local.get $k) (local.get $N)))
          ;; aik = A[i*N + k]
          (local.set $aik (f64.load
            (i32.add (local.get $Abase)
              (i32.mul (i32.const 8)
                (i32.add (i32.mul (local.get $i) (local.get $N)) (local.get $k))))))
          ;; bkj = B[k*N + j]
          (local.set $bkj (f64.load
            (i32.add (local.get $Bbase)
              (i32.mul (i32.const 8)
                (i32.add (i32.mul (local.get $k) (local.get $N)) (local.get $j))))))
          (local.set $acc (f64.add (local.get $acc) (f64.mul (local.get $aik) (local.get $bkj))))
          (local.set $k (i32.add (local.get $k) (i32.const 1)))
          (br $k_top)
        ))
        ;; C[i*N + j] = acc
        (f64.store
          (i32.add (local.get $Cbase)
            (i32.mul (i32.const 8)
              (i32.add (i32.mul (local.get $i) (local.get $N)) (local.get $j))))
          (local.get $acc))
        (local.set $j (i32.add (local.get $j) (i32.const 1)))
        (br $j_top)
      ))
      (local.set $i (i32.add (local.get $i) (i32.const 1)))
      (br $i_top)
    ))
  )

  ;; r = sum_i A[i] * B[i]  over count elements.
  (func $reduce (export "reduce") (param $count i32) (result f64)
    (local $i i32) (local $pa i32) (local $pb i32) (local $acc f64)
    (local.set $pa (i32.const 0))
    (local.set $pb (i32.mul (local.get $count) (i32.const 8)))
    (local.set $acc (f64.const 0))
    (local.set $i (i32.const 0))
    (block $done (loop $top
      (br_if $done (i32.ge_s (local.get $i) (local.get $count)))
      (local.set $acc
        (f64.add (local.get $acc)
          (f64.mul (f64.load (local.get $pa)) (f64.load (local.get $pb)))))
      (local.set $pa (i32.add (local.get $pa) (i32.const 8)))
      (local.set $pb (i32.add (local.get $pb) (i32.const 8)))
      (local.set $i (i32.add (local.get $i) (i32.const 1)))
      (br $top)
    ))
    (local.get $acc)
  )

  ;; Entry: fill two 180×180 f64 matrices, run matmul(180), then reduce over
  ;; the 180×180 products. 180 picked so the product fits comfortably in 1 MiB
  ;; (3 matrices × 180² × 8 = ~777 KB) and the inner loop runs 180³ = 5.8M
  ;; times, giving the profiler a meaty f64 workload.
  (func $run (export "run")
    (local $N i32) (local $NN i32)
    (local.set $N (i32.const 180))
    (local.set $NN (i32.mul (local.get $N) (local.get $N)))
    (call $fill (i32.const 0) (local.get $NN))
    (call $fill (i32.mul (local.get $NN) (i32.const 8)) (local.get $NN))
    (call $matmul (local.get $N))
    (drop (call $reduce (local.get $NN)))
  )

  (start $run)
)
