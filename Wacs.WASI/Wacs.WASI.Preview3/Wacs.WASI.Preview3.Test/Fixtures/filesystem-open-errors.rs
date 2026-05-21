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

async fn test_open_errors(dir: &Descriptor) {
    let open = |flags: PathFlags, path: &str| -> _ {
        dir.open_at(
            flags,
            path.to_string(),
            OpenFlags::empty(),
            DescriptorFlags::READ,
        )
    };
    let open_r = |path: &str| open(PathFlags::empty(), path);
    let open_r_follow = |path: &str| open(PathFlags::SYMLINK_FOLLOW, path);
    // open-at: async func(path-flags: path-flags, path: string, open-flags: open-flags, %flags: descriptor-flags) -> result<descriptor, error-code>;
    assert_eq!(open_r("").await.expect_err("open"), ErrorCode::NoEntry);
    assert_eq!(
        open_r("..").await.expect_err("open .."),
        ErrorCode::NotPermitted
    );
    assert_eq!(
        open_r_follow("parent").await.expect_err("open parent"),
        ErrorCode::NotPermitted
    );
    assert_eq!(
        open_r("/").await.expect_err("open /"),
        ErrorCode::NotPermitted
    );
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_open_errors(dir).await;
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
