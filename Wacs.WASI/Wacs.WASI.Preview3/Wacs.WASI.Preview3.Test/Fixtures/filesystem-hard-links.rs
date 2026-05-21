use std::process;
extern crate wit_bindgen;

wit_bindgen::generate!({
    inline: r"
  package test:test;

  world test {
      include wasi:filesystem/imports@0.3.0-rc-2026-03-15;
      include wasi:cli/command@0.3.0-rc-2026-03-15;
  }
",
    additional_derives: [PartialEq, Eq, Hash, Clone],
    // Work around https://github.com/bytecodealliance/wasm-tools/issues/2285.
    features:["clocks-timezone"],
    generate_all
});

use wasi::filesystem::types::Descriptor;
use wasi::filesystem::types::{ErrorCode, PathFlags};

async fn test_hard_links(dir: &Descriptor) {
    let ln_with_flags = |flags: PathFlags, from: &str, to: &str| -> _ {
        dir.link_at(flags, from.to_string(), dir, to.to_string())
    };
    let ln = |from: &str, to: &str| -> _ { ln_with_flags(PathFlags::empty(), from, to) };
    let rm = |path: &str| dir.unlink_file_at(path.to_string());
    let mkdir = |path: &str| dir.create_directory_at(path.to_string());
    let rmdir = |path: &str| dir.remove_directory_at(path.to_string());

    // link-at: async func(old-path-flags: path-flags, old-path: string, new-descriptor: borrow<descriptor>, new-path: string) -> result<_, error-code>;
    assert!(matches!(
        ln(".", "foo").await,
        Err(ErrorCode::NotPermitted | ErrorCode::Access)
    ));

    assert_eq!(ln("", "foo").await, Err(ErrorCode::NoEntry));
    assert_eq!(ln("", "").await, Err(ErrorCode::NoEntry));
    assert_eq!(ln("a.txt", "").await, Err(ErrorCode::NoEntry));
    assert_eq!(ln("a.txt", "a.txt").await, Err(ErrorCode::Exist));
    assert_eq!(ln("/", "a.txt").await, Err(ErrorCode::NotPermitted));
    assert_eq!(ln("a.txt", "/").await, Err(ErrorCode::NotPermitted));
    assert_eq!(ln("a.txt", "/a.txt").await, Err(ErrorCode::NotPermitted));
    assert_eq!(ln("..", "a.txt").await, Err(ErrorCode::NotPermitted));
    assert_eq!(ln("a.txt", "..").await, Err(ErrorCode::NotPermitted));
    // FIXME: https://github.com/WebAssembly/WASI/issues/710
    // assert_eq!(ln_follow("parent/foo", "a.txt").await,
    //            Err(ErrorCode::NotPermitted));
    assert_eq!(
        ln("parent/foo", "a.txt").await,
        Err(ErrorCode::NotPermitted)
    );
    assert_eq!(
        ln("a.txt", "parent/foo").await,
        Err(ErrorCode::NotPermitted)
    );
    ln("a.txt", "c.cleanup").await.unwrap();
    rm("c.cleanup").await.unwrap();
    mkdir("d.cleanup").await.unwrap();
    ln("a.txt", "d.cleanup/q.txt").await.unwrap();
    rm("d.cleanup/q.txt").await.unwrap();
    assert!(matches!(
        ln("d.cleanup", "e.cleanup").await,
        Err(ErrorCode::NotPermitted | ErrorCode::Access)
    ));
    rmdir("d.cleanup").await.unwrap();
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_hard_links(dir).await;
            }
            [..] => {
                eprintln!("usage: run with one open dir named 'fs-tests.dir'");
                process::exit(1)
            }
        };
        Ok(())
    }
}

fn main() {
    unreachable!("main is a stub");
}
