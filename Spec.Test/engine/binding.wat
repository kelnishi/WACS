(module
    (type (;0;) (func (param i32 i64 f32 f64) (result i32 i64 f32 f64)))
    (type (;1;) (func (param i32) (result i32)))
    (type (;2;) (func (param f32) (result f32)))
    (type (;3;) (func (param i32) (result i32 i32)))
    (type (;4;) (func (param i32 i32)))
    (type (;5;) (func (param i32)))
    (type (;6;) (func))
    (type (;7;) (func (param i32 i64 f32 f64) (result i32)))
    (import (;0;) "env" "bound_host" (func (;0;) (type 3)))
    (import (;1;) "env" "bound_async_host" (func (;1;) (type 1)))
    (func (;2;) (type 1)
        (param i32) (result i32)
        local.get 0
        i32.const 2
        i32.mul
    )
    (func (;3;) (type 2)
        (param f32) (result f32)
        local.get 0
        f32.const 2
        f32.mul
    )
    (func (;4;) (type 1)
        (param i32) (result i32)
        local.get 0
        call 0
        i32.add
    )
    (func (;5;) (type 0)
        (param i32 i64 f32 f64) (result i32 i64 f32 f64)
        local.get 0
        i32.const 2
        i32.mul
        local.get 1
        i64.const 3
        i64.mul
        local.get 2
        f32.const 4.0
        f32.mul
        local.get 3
        f64.const 5.0
        f64.mul
        return
    )
    (func (;6;) (type 1)
        (param i32) (result i32)
        local.get 0
        call 1
    )
    (func (;7;) (type 4)
       (param i32 i32)
       local.get 1
       call 0
       drop
       drop
    )
    (func (;8;) (type 5)
       (param i32)
       local.get 0
       call 0
       drop
       drop
    )
    (func (;9;) (type 6)
       i32.const 6
       call 0
       drop
       drop
    )
    (func (;10;) (type 7)
        (param i32 i64 f32 f64) (result i32)
        local.get 0
        return
    )
    (export "i32" (func 2))
    (export "f32" (func 3))
    (export "call_host" (func 4))
    (export "4x4" (func 5))
    (export "call_async_host" (func 6))
    (export "2x0" (func 7))
    (export "1x0" (func 8))
    (export "0x0" (func 9))
    (export "4x1" (func 10))
)