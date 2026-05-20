use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        Err(())
    }
}

fn main() {
    unreachable!()
}
