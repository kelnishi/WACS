// WASIp3 vertical-slice acceptance fixture for WACS.WASI.Preview3.
//
// Implements wasi:cli/run with a single async run() that writes
// "hello, wasip3\n" to wasi:cli/stdout. Mirrors the canonical
// "hello world" component-model program used in the runtime
// validation matrix.
//
// Pairs with cli-hello.json (operations sidecar): a `run` op
// followed by a `read` on stdout asserting the captured payload.

use test_wasm32_wasip3::cli::{
    export,
    exports::wasi::cli::run::Guest,
    wasi::cli::stdout,
    wit_stream,
};

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        let (mut tx, rx) = wit_stream::new();
        futures::join!(
            async {
                stdout::write_via_stream(rx).await.unwrap();
            },
            async {
                let remaining = tx.write_all(b"hello, wasip3\n".to_vec()).await;
                assert!(remaining.is_empty());
                drop(tx);
            }
        );
        Ok(())
    }
}

fn main() {
    unreachable!()
}
