wit_bindgen::generate!({
    path: "wit",
    world: "app",
});

use std::cell::Cell;
use std::sync::atomic::{AtomicU32, Ordering};
use exports::wacs::canon_resource_test::counter::{
    Counter as CounterResource, Guest as CounterGuest, GuestCounter,
};

// Observable side-channel so the validator can confirm the
// dtor actually fires when the runtime drops a handle.
static DROP_COUNT: AtomicU32 = AtomicU32::new(0);

struct MyCounter { value: Cell<u32> }

impl Drop for MyCounter {
    fn drop(&mut self) {
        DROP_COUNT.fetch_add(1, Ordering::SeqCst);
    }
}

impl GuestCounter for MyCounter {
    fn new(initial: u32) -> Self { Self { value: Cell::new(initial) } }
    fn bump(&self) -> u32 {
        let v = self.value.get() + 1;
        self.value.set(v);
        v
    }
}

struct Example;

impl CounterGuest for Example {
    type Counter = MyCounter;
}

export!(Example);
