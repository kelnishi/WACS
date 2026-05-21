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

use wasi::filesystem::types::Advice;
use wasi::filesystem::types::Descriptor;
use wasi::filesystem::types::{DescriptorFlags, ErrorCode, OpenFlags, PathFlags};

async fn test_advise(dir: &Descriptor) {
    // Advise should fail on directories.
    assert_eq!(
        dir.advise(0, 0, Advice::Normal).await,
        Err(ErrorCode::BadDescriptor)
    );

    let fd = dir
        .open_at(
            PathFlags::empty(),
            "a.txt".to_string(),
            OpenFlags::empty(),
            DescriptorFlags::READ,
        )
        .await
        .unwrap();

    let size = fd.stat().await.unwrap().size;
    for advice in [
        Advice::Normal,
        Advice::Sequential,
        Advice::Random,
        Advice::WillNeed,
        Advice::DontNeed,
        Advice::NoReuse,
    ] {
        for offset in [0, size.saturating_sub(4096), size, size + 4096] {
            for length in [0, 10, 4096, size, size + 4096] {
                match fd.advise(offset, length, advice).await {
                    Ok(()) => {}
                    Err(err) => {
                        eprintln!("fadvise({}, {}, {:?}) => {}", offset, length, advice, err);
                        process::exit(1);
                    }
                }
            }
        }
    }
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_advise(dir).await;
            }
            [..] => {
                eprintln!("usage: run with one open dir named 'fs-tests.dir'");
                process::exit(1)
            }
        }
        Ok(())
    }
}

fn main() {
    unreachable!("main is a stub");
}
