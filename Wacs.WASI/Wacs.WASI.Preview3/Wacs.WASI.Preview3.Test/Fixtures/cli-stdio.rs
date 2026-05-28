use test_wasm32_wasip3::cli::{
    export,
    exports::wasi::cli::run::Guest,
    wasi::cli::{stderr, stdin, stdout},
    wit_stream,
};

use wit_bindgen::StreamResult;

struct Component;
export!(Component);

async fn test_stdin_drop_readable_end() {
    let (stdin, result) = stdin::read_via_stream();
    drop(stdin);
    result.await.unwrap();
}

async fn test_stdout_drop_writable_end() {
    let (mut stdout_tx, stdout_rx) = wit_stream::new();
    futures::join!(
        async {
            assert!(stdout::write_via_stream(stdout_rx).await.is_ok());
        },
        async {
            let (res, buf) = stdout_tx.write(vec![0; 1]).await;
            assert_eq!(res, StreamResult::Complete(1));
            assert_eq!(buf.into_vec(), []);
            drop(stdout_tx);
        }
    );
}

async fn test_stderr_drop_writable_end() {
    let (mut stderr_tx, stderr_rx) = wit_stream::new();
    futures::join!(
        async {
            assert!(stderr::write_via_stream(stderr_rx).await.is_ok());
        },
        async {
            let (res, buf) = stderr_tx.write(vec![0; 1]).await;
            assert_eq!(res, StreamResult::Complete(1));
            assert_eq!(buf.into_vec(), []);
            drop(stderr_tx);
        }
    );
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_stdin_drop_readable_end().await;
        test_stdout_drop_writable_end().await;
        test_stderr_drop_writable_end().await;
        Ok(())
    }
}

fn main() {
    unreachable!();
}
