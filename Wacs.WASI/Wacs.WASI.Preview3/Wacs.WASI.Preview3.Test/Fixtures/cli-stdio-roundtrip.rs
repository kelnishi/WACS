use test_wasm32_wasip3::cli::{
    export,
    exports::wasi::cli::run::Guest,
    wasi::cli::{stderr, stdin, stdout},
    wit_stream,
};

use wit_bindgen::StreamResult;

struct Component;
export!(Component);

macro_rules! write_all {
    ($payload:expr, $write_fn:path) => {{
        let (mut tx, rx) = wit_stream::new();
        futures::join!(
            async {
                $write_fn(rx).await.unwrap();
            },
            async {
                let remaining = tx.write_all($payload).await;
                assert!(remaining.is_empty());
                drop(tx);
            }
        );
    }};
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        let (mut stdin, stdin_result) = stdin::read_via_stream();
        // The tests is correctly configured with a payload
        // that fits in 13 bytes.
        let (result, payload) = stdin.read(Vec::with_capacity(13)).await;
        assert_eq!(result, StreamResult::Complete(13));

        drop(stdin);
        stdin_result.await.unwrap();

        write_all!(payload.clone(), stdout::write_via_stream);
        write_all!(payload, stderr::write_via_stream);

        Ok(())
    }
}

fn main() {
    unreachable!()
}
