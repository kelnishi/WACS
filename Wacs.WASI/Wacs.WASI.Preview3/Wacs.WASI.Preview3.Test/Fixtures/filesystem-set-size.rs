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
use wasi::filesystem::types::{DescriptorFlags, ErrorCode, OpenFlags, PathFlags};

async fn test_set_size(dir: &Descriptor) {
    // set-size: async func(size: filesize) -> result<_, error-code>;
    let open = |path: &str, oflags: OpenFlags, fdflags: DescriptorFlags| -> _ {
        dir.open_at(PathFlags::empty(), path.to_string(), oflags, fdflags)
    };
    let open_r = |path: &str| -> _ { open(path, OpenFlags::empty(), DescriptorFlags::READ) };
    let open_w = |path: &str| -> _ {
        open(
            path,
            OpenFlags::empty(),
            DescriptorFlags::READ | DescriptorFlags::WRITE,
        )
    };
    let creat = |path: &str| -> _ {
        open(
            path,
            OpenFlags::CREATE | OpenFlags::EXCLUSIVE,
            DescriptorFlags::READ | DescriptorFlags::WRITE,
        )
    };
    let trunc = |path: &str| -> _ {
        open(
            path,
            OpenFlags::TRUNCATE,
            DescriptorFlags::READ | DescriptorFlags::WRITE,
        )
    };
    let rm = |path: &str| dir.unlink_file_at(path.to_string());

    let c = creat("c.cleanup").await.unwrap();
    assert_eq!(c.stat().await.unwrap().size, 0);
    c.set_size(42).await.unwrap();
    // Setting size is visible immediately.
    assert_eq!(c.stat().await.unwrap().size, 42);

    let c = open_w("c.cleanup").await.unwrap();
    let r = open_r("c.cleanup").await.unwrap();
    assert_eq!(c.stat().await.unwrap().size, 42);
    assert_eq!(r.stat().await.unwrap().size, 42);
    c.set_size(69).await.unwrap();
    assert_eq!(c.stat().await.unwrap().size, 69);
    assert_eq!(r.stat().await.unwrap().size, 69);

    let c = trunc("c.cleanup").await.unwrap();
    assert_eq!(c.stat().await.unwrap().size, 0);
    assert_eq!(r.stat().await.unwrap().size, 0);

    // https://github.com/WebAssembly/WASI/issues/712
    match r.set_size(100).await {
        Ok(()) => {
            panic!("set-size succeeded on read-only descriptor");
        }
        Err(ErrorCode::Invalid | ErrorCode::BadDescriptor | ErrorCode::Access) => {}
        Err(err) => {
            panic!("unexpected err: {}", err)
        }
    };

    // https://github.com/WebAssembly/WASI/issues/712
    match c.set_size(u64::MAX).await {
        Ok(()) => {
            panic!("set-size(-1) succeeded");
        }
        Err(ErrorCode::Invalid | ErrorCode::FileTooLarge) => {}
        Err(err) => {
            panic!("unexpected err: {}", err)
        }
    };

    match rm("c.cleanup").await {
        Ok(()) => {
            // We still have `c` and `r` open, which refer to the file,
            // but we can still stat our descriptors, call `set-size` on
            // it, and so on.
            assert_eq!(c.stat().await.unwrap().size, 0);
            c.set_size(42).await.unwrap();
            assert_eq!(c.stat().await.unwrap().size, 42);
            assert_eq!(r.stat().await.unwrap().size, 42);
        }
        Err(ErrorCode::Busy) => {
            // Otherwise if we're on Windows we are unable to remove the
            // file while descriptors are open.
        }
        Err(err) => {
            panic!("unexpected err: {}", err)
        }
    }
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_set_size(dir).await;
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
