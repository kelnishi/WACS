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
use wasi::filesystem::types::{DescriptorFlags, ErrorCode, OpenFlags, PathFlags};

async fn test_flags_and_type(dir: &Descriptor) {
    // get-flags: async func() -> result<descriptor-flags, error-code>;
    // get-type: async func() -> result<descriptor-type, error-code>;
    let open = |path: &str, oflags: OpenFlags, fdflags: DescriptorFlags| -> _ {
        dir.open_at(PathFlags::empty(), path.to_string(), oflags, fdflags)
    };
    let open_r = |path: &str| -> _ { open(path, OpenFlags::empty(), DescriptorFlags::READ) };
    let creat = |path: &str| -> _ {
        open(
            path,
            OpenFlags::CREATE | OpenFlags::EXCLUSIVE,
            DescriptorFlags::READ | DescriptorFlags::WRITE,
        )
    };
    let rm = |path: &str| dir.unlink_file_at(path.to_string());

    assert_eq!(
        dir.get_flags().await,
        Ok(DescriptorFlags::READ | DescriptorFlags::MUTATE_DIRECTORY)
    );
    assert_eq!(dir.get_type().await, Ok(DescriptorType::Directory));

    let d = open(".", OpenFlags::empty(), DescriptorFlags::empty())
        .await
        .unwrap();
    // No flags opening dir?  Open as read.
    assert_eq!(d.get_flags().await, Ok(DescriptorFlags::READ));
    assert_eq!(d.get_type().await, Ok(DescriptorType::Directory));

    assert_eq!(
        open(".", OpenFlags::empty(), DescriptorFlags::WRITE)
            .await
            .unwrap_err(),
        ErrorCode::IsDirectory
    );

    // FIXME: https://github.com/bytecodealliance/wasmtime/issues/11667
    // let d = open(".", OpenFlags::empty(),
    //              DescriptorFlags::MUTATE_DIRECTORY).await.unwrap();
    // assert_eq!(d.get_flags().await,
    //            Ok(DescriptorFlags::READ | DescriptorFlags::MUTATE_DIRECTORY));
    // assert_eq!(d.get_type().await,
    //            Ok(DescriptorType::Directory));
    //
    // let d = open(".", OpenFlags::empty(),
    //              DescriptorFlags::READ | DescriptorFlags::MUTATE_DIRECTORY).await.unwrap();
    // assert_eq!(d.get_flags().await,
    //            Ok(DescriptorFlags::READ | DescriptorFlags::MUTATE_DIRECTORY));
    // assert_eq!(d.get_type().await,
    //            Ok(DescriptorType::Directory));
    // let d = open(".", OpenFlags::DIRECTORY,
    //              DescriptorFlags::READ | DescriptorFlags::MUTATE_DIRECTORY).await.unwrap();
    // assert_eq!(d.get_flags().await,
    //            Ok(DescriptorFlags::READ | DescriptorFlags::MUTATE_DIRECTORY));
    // assert_eq!(d.get_type().await,
    //            Ok(DescriptorType::Directory));

    let a = open_r("a.txt").await.unwrap();
    assert_eq!(a.get_flags().await, Ok(DescriptorFlags::READ));
    assert_eq!(a.get_type().await, Ok(DescriptorType::RegularFile));
    let c = creat("c.cleanup").await.unwrap();
    assert_eq!(
        c.get_flags().await,
        Ok(DescriptorFlags::READ | DescriptorFlags::WRITE)
    );
    rm("c.cleanup").await.unwrap();

    let c = open(
        "c.cleanup",
        OpenFlags::CREATE | OpenFlags::EXCLUSIVE,
        DescriptorFlags::empty(),
    )
    .await
    .unwrap();
    // CREATE implies WRITE.
    assert_eq!(
        c.get_flags().await,
        Ok(DescriptorFlags::READ | DescriptorFlags::WRITE)
    );
    rm("c.cleanup").await.unwrap();

    let c = open(
        "c.cleanup",
        OpenFlags::CREATE | OpenFlags::EXCLUSIVE,
        DescriptorFlags::READ,
    )
    .await
    .unwrap();
    // CREATE implies WRITE.
    assert_eq!(
        c.get_flags().await,
        Ok(DescriptorFlags::READ | DescriptorFlags::WRITE)
    );
    rm("c.cleanup").await.unwrap();

    let c = open(
        "c.cleanup",
        OpenFlags::CREATE | OpenFlags::EXCLUSIVE,
        DescriptorFlags::WRITE,
    )
    .await
    .unwrap();
    // CREATE implies WRITE, but not READ.
    assert_eq!(c.get_flags().await, Ok(DescriptorFlags::WRITE));
    rm("c.cleanup").await.unwrap();

    // EXCLUSIVE is meaningless without CREATE; flags default to READ.
    let a = open("a.txt", OpenFlags::EXCLUSIVE, DescriptorFlags::empty())
        .await
        .unwrap();
    assert_eq!(a.get_flags().await, Ok(DescriptorFlags::READ));

    // No flags?  Default to READ.
    let a = open("a.txt", OpenFlags::empty(), DescriptorFlags::empty())
        .await
        .unwrap();
    assert_eq!(a.get_flags().await, Ok(DescriptorFlags::READ));

    // WRITE does not imply READ.
    let a = open("a.txt", OpenFlags::empty(), DescriptorFlags::WRITE)
        .await
        .unwrap();
    assert_eq!(a.get_flags().await, Ok(DescriptorFlags::WRITE));
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_flags_and_type(dir).await;
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
