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
use wasi::filesystem::types::DescriptorType;
use wasi::filesystem::types::DirectoryEntry;

async fn test_read_directory(dir: &Descriptor) {
    // read-directory: async func() -> tuple<stream<directory-entry>, future<result<_, error-code>>>;
    let (stream, result) = dir.read_directory();
    let mut entries = stream.collect().await;
    result.await.unwrap();
    entries.sort_by_key(|e| e.name.clone());
    assert_eq!(
        &entries,
        &[
            DirectoryEntry {
                type_: DescriptorType::RegularFile,
                name: "a.txt".to_string()
            },
            DirectoryEntry {
                type_: DescriptorType::RegularFile,
                name: "b.txt".to_string()
            },
            DirectoryEntry {
                type_: DescriptorType::SymbolicLink,
                name: "parent".to_string()
            }
        ]
    );
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_read_directory(dir).await;
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
