(module
  (import "wasi:http/types@0.2.3" "http-error-code"
    (func $hec (param i32 i32)))
  (memory (export "memory") 1)
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
  ;; option<error-code> retArea: 40 bytes (align 8 from option<u64>).
  ;; Layout:
  ;;   0:  outer option disc
  ;;   1-7: padding
  ;;   8:  error-code variant disc
  ;;   9-15: padding (variant payload starts at +16)
  ;;   16+: variant payload area (24 bytes)
  (func (export "ask-http-error-code") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 40)))
    (call $hec (local.get 0) (local.get $r))
    (i32.load8_u (local.get $r)))
  ;; Returns the inner error-code variant disc (byte at +8).
  (func (export "ask-variant-disc") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 40)))
    (call $hec (local.get 0) (local.get $r))
    (i32.load8_u offset=8 (local.get $r)))
  ;; Returns the inner option<u64> value at variant-payload+8
  ;; (= retArea + 16 + 8 = retArea + 24). Used for cases like
  ;; HTTP-request-body-size that carry option<u64>.
  (func (export "ask-payload-u64") (param i32) (result i64)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 40)))
    (call $hec (local.get 0) (local.get $r))
    (i64.load offset=24 (local.get $r)))
  ;; Returns the disc byte of the inner option (e.g. for
  ;; option<u64> payload, byte at variant-payload+0 =
  ;; retArea + 16). 0 = None, 1 = Some.
  (func (export "ask-payload-option-disc") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 40)))
    (call $hec (local.get 0) (local.get $r))
    (i32.load8_u offset=16 (local.get $r)))
  ;; Returns the option<string> ptr at variant-payload+4
  ;; (= retArea + 20). Used for cases like internal-error
  ;; that carry option<string>.
  (func (export "ask-payload-string-ptr") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 40)))
    (call $hec (local.get 0) (local.get $r))
    (i32.load offset=20 (local.get $r)))
  ;; Returns the option<string> len at variant-payload+8
  ;; (= retArea + 24).
  (func (export "ask-payload-string-len") (param i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 8) (i32.const 40)))
    (call $hec (local.get 0) (local.get $r))
    (i32.load offset=24 (local.get $r)))
  ;; Reads one byte from memory at the given pointer — used
  ;; by tests to probe the allocated UTF-8 buffer for an
  ;; option<string> payload.
  (func (export "read-byte") (param i32) (result i32)
    (i32.load8_u (local.get 0))))
