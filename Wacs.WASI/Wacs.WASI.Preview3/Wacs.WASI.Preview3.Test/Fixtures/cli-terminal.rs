use test_wasm32_wasip3::cli::{
    export,
    exports::wasi::cli::run::Guest,
    wasi::cli::{terminal_stderr, terminal_stdin, terminal_stdout},
};

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        assert!(terminal_stderr::get_terminal_stderr().is_none());
        assert!(terminal_stdout::get_terminal_stdout().is_none());
        assert!(terminal_stdin::get_terminal_stdin().is_none());
        Ok(())
    }
}

fn main() {
    unreachable!()
}
