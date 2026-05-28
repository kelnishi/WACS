use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::clocks::wasi::clocks::system_clock;

const NANOSECOND: u32 = 1;
const MICROSECOND: u32 = NANOSECOND * 1_000;
const MILLISECOND: u32 = MICROSECOND * 1_000;
const SECOND: u32 = MILLISECOND * 1_000;

fn verify_instant(t: system_clock::Instant) {
    assert!(t.nanoseconds < SECOND)
}

fn main() {
    unreachable!();
}

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        // Not much we can assert about system-clock time.
        verify_instant(system_clock::now());
        let resolution = system_clock::get_resolution();
        let resolution_instant = system_clock::Instant {
            seconds: 0,
            nanoseconds: resolution as u32,
        };
        verify_instant(resolution_instant);
        // Resolution should be non-zero and represent the clock's precision.
        assert!(resolution > 0, "Clock resolution should be non-zero");
        Ok(())
    }
}
