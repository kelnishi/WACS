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

use wasi::filesystem::types::{Descriptor, DescriptorFlags, ErrorCode, OpenFlags, PathFlags};

async fn test_rename(dir: &Descriptor) {
    // rename-at: async func(old-path: string, new-descriptor: borrow<descriptor>, new-path: string) -> result<_, error-code>;
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
    let mv = |from: &str, to: &str| -> _ { dir.rename_at(from.to_string(), dir, to.to_string()) };
    let mv_to_child =
        |from: &str, to: &str| -> _ { dir.rename_at(from.to_string(), &child, to.to_string()) };
    let mv_from_child =
        |from: &str, to: &str| -> _ { child.rename_at(from.to_string(), dir, to.to_string()) };
    let ln_s = |from: &str, to: &str| -> _ { dir.symlink_at(from.to_string(), to.to_string()) };

    mv("a.txt", "a.txt").await.unwrap();
    ln_s("a.txt", "c.cleanup").await.unwrap();
    mv("a.txt", "c.cleanup").await.unwrap();
    assert_eq!(mv("a.txt", "a.txt").await, Err(ErrorCode::NoEntry));
    mv("c.cleanup", "a.txt").await.unwrap();
    assert_eq!(mv("c.cleanup", "a.txt").await, Err(ErrorCode::NoEntry));
    assert_eq!(
        mv("does-not-exist.txt", "q.txt").await,
        Err(ErrorCode::NoEntry)
    );
    match mv(".", "q.txt").await {
        Err(ErrorCode::Busy | ErrorCode::Invalid | ErrorCode::Access) => {}
        Ok(()) => {
            panic!("mv . q.txt unexpectedly succeeded");
        }
        Err(err) => {
            panic!("mv . q.txt: unexpected error {}", err);
        }
    };
    mv("a.txt", "c.cleanup").await.unwrap();
    assert_eq!(mv("a.txt", "q.txt").await, Err(ErrorCode::NoEntry));
    mv("c.cleanup", "a.txt").await.unwrap();
    assert_eq!(mv("a.txt", "../q.txt").await, Err(ErrorCode::NotPermitted));
    assert_eq!(
        mv("a.txt", "parent/q.txt").await,
        Err(ErrorCode::NotPermitted)
    );
    assert_eq!(
        mv("a.txt", "/tmp/q.txt").await,
        Err(ErrorCode::NotPermitted)
    );

    mv_to_child("a.txt", "whatever").await.unwrap();
    assert_eq!(mv("a.txt", "whatever").await, Err(ErrorCode::NoEntry));
    assert_eq!(mv("whatever", "whatever").await, Err(ErrorCode::NoEntry));
    assert_eq!(
        mv_to_child("a.txt", "whatever").await,
        Err(ErrorCode::NoEntry)
    );
    mv_from_child("whatever", "a.txt").await.unwrap();
    assert_eq!(
        mv_from_child("whatever", "a.txt").await,
        Err(ErrorCode::NoEntry)
    );

    drop(child);
    dir.remove_directory_at("child.cleanup".to_string())
        .await
        .unwrap();
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_rename(dir).await;
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
