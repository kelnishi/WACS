use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::random::wasi::random::{insecure, insecure_seed, random};

struct Component;

export!(Component);

fn test_get_random_bytes() {
    assert_eq!(random::get_random_bytes(100).len(), 100);
    let a = random::get_random_bytes(10);
    let b = random::get_random_bytes(10);
    // Should always return fresh data.
    assert_ne!(a, b);
}

fn test_get_random_u64() {
    let a = random::get_random_u64();
    let b = random::get_random_u64();
    // Should always return fresh data.
    assert_ne!(a, b);
}

fn test_get_insecure_seed() {
    let (a, b) = insecure_seed::get_insecure_seed();
    let (a1, b1) = insecure_seed::get_insecure_seed();

    // Meant to be only called once.
    // Any subsequent calls should return the same value.
    assert_eq!(a, a1);
    assert_eq!(b, b1);
}

fn test_get_insecure_random_bytes() {
    let a = insecure::get_insecure_random_bytes(100);
    let b = insecure::get_insecure_random_bytes(100);
    assert_eq!(a.len(), 100);
    assert_ne!(a, b);
}

fn test_get_insecure_random_u64() {
    let a = insecure::get_insecure_random_u64();
    let b = insecure::get_insecure_random_u64();
    assert_ne!(a, b);
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_get_random_bytes();
        test_get_random_u64();
        test_get_insecure_seed();
        test_get_insecure_random_bytes();
        test_get_insecure_random_u64();
        Ok(())
    }
}

fn main() {
    unreachable!();
}
