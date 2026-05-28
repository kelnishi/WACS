use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest, wasi::cli::exit::exit};

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        exit(Err(()));
        Ok(())
    }
}

fn main() {
    unreachable!()
}
