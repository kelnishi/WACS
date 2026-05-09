(module
  (import "wasi:sockets/tcp@0.2.8" "[method]tcp-socket.start-bind"
    (func $sb (param i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32 i32)))
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
  (func (export "ask-bind") (param i32 i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    ;; Call start-bind with ipv4 disc=0, port=9000, address=127.0.0.1.
    ;; Slots: self, network, disc=0, s1=port=9000, s2=127, s3=0, s4=0, s5=1,
    ;;        s6..s11 = 0 (don't care for ipv4), retArea
    (call $sb (local.get 0) (local.get 1)
              (i32.const 0)        ;; variant disc = ipv4
              (i32.const 9000)     ;; port
              (i32.const 127)      ;; addr[0]
              (i32.const 0)        ;; addr[1]
              (i32.const 0)        ;; addr[2]
              (i32.const 1)        ;; addr[3]
              (i32.const 0)        ;; padding/ignored
              (i32.const 0)
              (i32.const 0)
              (i32.const 0)
              (i32.const 0)
              (i32.const 0)
              (local.get $r))
    (i32.load8_u (local.get $r)))
  (func (export "ask-bind-v6") (param i32 i32) (result i32)
    (local $r i32)
    (local.set $r (call $realloc (i32.const 0) (i32.const 0) (i32.const 1) (i32.const 2)))
    ;; ipv6: disc=1, s1=port=443, s2=flow=0, s3..s10 = 8 u16 groups,
    ;;       s11=scope=0. Address [::1] = 0,0,0,0,0,0,0,1.
    (call $sb (local.get 0) (local.get 1)
              (i32.const 1)        ;; variant disc = ipv6
              (i32.const 443)      ;; port
              (i32.const 0)        ;; flow-info
              (i32.const 0)        ;; addr[0]
              (i32.const 0)        ;; addr[1]
              (i32.const 0)        ;; addr[2]
              (i32.const 0)        ;; addr[3]
              (i32.const 0)        ;; addr[4]
              (i32.const 0)        ;; addr[5]
              (i32.const 0)        ;; addr[6]
              (i32.const 1)        ;; addr[7]
              (i32.const 0)        ;; scope-id
              (local.get $r))
    (i32.load8_u (local.get $r))))
