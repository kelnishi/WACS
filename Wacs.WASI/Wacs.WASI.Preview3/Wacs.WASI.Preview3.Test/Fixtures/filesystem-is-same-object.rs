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
use wasi::filesystem::types::{DescriptorFlags, OpenFlags, PathFlags};

async fn check_one(fd: &Descriptor, same: &[&Descriptor], different: &[&Descriptor]) {
    assert!(fd.is_same_object(fd).await);
    for other in same {
        assert!(fd.is_same_object(other).await);
    }
    for other in different {
        assert!(!fd.is_same_object(other).await);
    }
}

async fn test_is_same_object(dir: &Descriptor) {
    let afd = dir
        .open_at(
            PathFlags::empty(),
            "a.txt".to_string(),
            OpenFlags::empty(),
            DescriptorFlags::READ,
        )
        .await
        .unwrap();
    let bfd = dir
        .open_at(
            PathFlags::empty(),
            "b.txt".to_string(),
            OpenFlags::empty(),
            DescriptorFlags::READ,
        )
        .await
        .unwrap();

    dir.link_at(
        PathFlags::empty(),
        "a.txt".to_string(),
        dir,
        "c.cleanup".to_string(),
    )
    .await
    .unwrap();

    let cfd = dir
        .open_at(
            PathFlags::empty(),
            "c.cleanup".to_string(),
            OpenFlags::empty(),
            DescriptorFlags::READ,
        )
        .await
        .unwrap();

    // is-same-object: async func(other: borrow<descriptor>) -> bool;
    check_one(dir, &[], &[&afd, &bfd, &cfd]).await;
    check_one(&afd, &[&cfd], &[dir, &bfd]).await;
    check_one(&bfd, &[], &[dir, &afd, &cfd]).await;
    check_one(&cfd, &[&afd], &[dir, &bfd]).await;

    {
        let other = dir
            .open_at(
                PathFlags::empty(),
                ".".to_string(),
                OpenFlags::empty(),
                DescriptorFlags::READ,
            )
            .await
            .unwrap();
        assert!(dir.is_same_object(&other).await);
    }
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_is_same_object(dir).await;
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
