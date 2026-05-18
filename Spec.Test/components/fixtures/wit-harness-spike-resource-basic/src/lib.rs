wit_bindgen::generate!({
    path: "wit",
    world: "app",
});

use std::sync::Mutex;
use exports::wacs::resource_basic_spike::store::{Bucket, Guest as StoreGuest, GuestBucket};

// Simple state to count drops, so the validator (via Rust-side
// observable behavior) can confirm Dispose calls actually fire.
static DROP_COUNT: Mutex<u32> = Mutex::new(0);

struct MyBucket { name: String }

impl Drop for MyBucket {
    fn drop(&mut self) {
        let mut c = DROP_COUNT.lock().unwrap();
        *c += 1;
    }
}

impl GuestBucket for MyBucket {}

struct Example;

impl StoreGuest for Example {
    type Bucket = MyBucket;

    fn open(name: String) -> Bucket {
        Bucket::new(MyBucket { name })
    }
}

export!(Example);
