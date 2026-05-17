wit_bindgen::generate!({
    path: "wit",
    world: "demo",
});

use std::cell::Cell;
use exports::wacs::resource_methods_spike::counter::{
    Counter as CounterResource, Guest as CounterGuest, GuestCounter,
};

struct MyCounter { value: Cell<u32> }

impl GuestCounter for MyCounter {
    fn new(initial: u32) -> Self { Self { value: Cell::new(initial) } }
    fn increment(&self) -> u32 { let v = self.value.get() + 1; self.value.set(v); v }
    fn get_value(&self) -> u32 { self.value.get() }
    fn merge(a: u32, b: u32) -> u32 { a.wrapping_add(b) }
}

struct Example;

impl CounterGuest for Example {
    type Counter = MyCounter;
}

export!(Example);
