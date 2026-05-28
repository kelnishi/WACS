use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest, wasi::cli::environment};

struct Component;
export!(Component);

fn test_get_environment() {
    let env = environment::get_environment();
    assert_eq!(env.len(), 2);

    for (k, v) in env {
        match k.as_str() {
            "foo" => assert_eq!(v, "bar"),
            "baz" => assert_eq!(v, "42"),
            unknown => panic!("Unexpected option {}", unknown),
        }
    }
}

fn test_get_arguments() {
    let args = environment::get_arguments();
    assert_eq!(args, vec!["cli-env.wasm", "a", "b", "42"]);
}

fn test_get_initial_cwd() {
    // FIXME:
    // Currently we can only test that the returned value is None, since the
    // currently implementation is incomplete. See
    // https://github.com/bytecodealliance/wasmtime/pull/9831 and
    // https://github.com/WebAssembly/wasi-testsuite/issues/216
    let cwd = environment::get_initial_cwd();
    assert!(cwd.is_none());
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_get_environment();
        test_get_arguments();
        test_get_initial_cwd();
        Ok(())
    }
}

fn main() {
    unreachable!()
}
