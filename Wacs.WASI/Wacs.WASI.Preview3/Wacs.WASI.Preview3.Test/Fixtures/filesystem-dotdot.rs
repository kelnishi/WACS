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

use wasi::filesystem::types::{DescriptorFlags, ErrorCode, OpenFlags, PathFlags};

async fn test_symlink() {
    match &wasi::filesystem::preopens::get_directories()[..] {
        [(dir, _)] => {
            dir.create_directory_at("child.cleanup".to_string())
                .await
                .unwrap();
            let child = dir
                .open_at(
                    PathFlags::empty(),
                    "child.cleanup".to_string(),
                    OpenFlags::DIRECTORY,
                    DescriptorFlags::MUTATE_DIRECTORY,
                )
                .await
                .unwrap();

            drop(
                dir.open_at(
                    PathFlags::empty(),
                    "x.cleanup".to_string(),
                    OpenFlags::CREATE | OpenFlags::EXCLUSIVE,
                    DescriptorFlags::READ,
                )
                .await
                .unwrap(),
            );

            drop(
                dir.open_at(
                    PathFlags::empty(),
                    "x.cleanup".to_string(),
                    OpenFlags::empty(),
                    DescriptorFlags::READ,
                )
                .await
                .unwrap(),
            );

            // Possibly revisit if
            // https://github.com/WebAssembly/WASI/issues/856 ever
            // changes things.
            assert_eq!(
                child
                    .open_at(
                        PathFlags::empty(),
                        "../x.cleanup".to_string(),
                        OpenFlags::empty(),
                        DescriptorFlags::READ
                    )
                    .await
                    .unwrap_err(),
                ErrorCode::NotPermitted
            );

            drop(child);
            dir.remove_directory_at("child.cleanup".to_string())
                .await
                .unwrap();
            dir.unlink_file_at("x.cleanup".to_string()).await.unwrap();
        }
        [..] => {
            eprintln!("usage: run with one open dir");
            process::exit(1)
        }
    }
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        test_symlink().await;
        Ok(())
    }
}

fn main() {
    unreachable!("main is a stub");
}
