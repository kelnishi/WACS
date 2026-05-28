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

async fn check_metadata_hash(a: &Descriptor, b: &Descriptor) {
    let a_hash = a.metadata_hash().await;
    let b_hash = b.metadata_hash().await;
    if a_hash.is_ok() && a_hash == b_hash {
        assert_eq!(a.stat().await.unwrap(), b.stat().await.unwrap());
    }
}

async fn check_metadata_hash_at(
    a: &Descriptor,
    a_flags: PathFlags,
    a_name: &str,
    b: &Descriptor,
    b_flags: PathFlags,
    b_name: &str,
) {
    let a_hash = a.metadata_hash_at(a_flags, a_name.to_string()).await;
    let b_hash = b.metadata_hash_at(b_flags, b_name.to_string()).await;
    if a_hash.is_ok() && a_hash == b_hash {
        assert_eq!(
            a.stat_at(a_flags, a_name.to_string()).await.unwrap(),
            b.stat_at(b_flags, b_name.to_string()).await.unwrap()
        );
    }
}

async fn test_metadata_hash(dir: &Descriptor) {
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
    // FIXME: https://github.com/bytecodealliance/wasmtime/issues/12172
    // dir.symlink_at(
    //     "../a.txt".to_string(),
    //     "child.cleanup/symlink.cleanup".to_string(),
    // )
    // .await
    // .unwrap();
    dir.link_at(
        PathFlags::empty(),
        "a.txt".to_string(),
        &child,
        "link.cleanup".to_string(),
    )
    .await
    .unwrap();
    let a_link = child
        .open_at(
            PathFlags::empty(),
            "link.cleanup".to_string(),
            OpenFlags::empty(),
            DescriptorFlags::READ,
        )
        .await
        .unwrap();

    // metadata-hash: async func() -> result<metadata-hash-value, error-code>;
    check_metadata_hash(dir, dir).await;
    check_metadata_hash(dir, &afd).await;
    check_metadata_hash(&afd, &afd).await;
    check_metadata_hash(&afd, &bfd).await;
    check_metadata_hash(&afd, &a_link).await;
    check_metadata_hash(&bfd, &a_link).await;

    // metadata-hash-at: async func(path-flags: path-flags, path: string) -> result<metadata-hash-value, error-code>;
    assert_eq!(
        dir.metadata_hash_at(PathFlags::empty(), "/".to_string())
            .await,
        Err(ErrorCode::NotPermitted)
    );
    assert_eq!(
        dir.metadata_hash_at(PathFlags::empty(), "".to_string())
            .await,
        Err(ErrorCode::NoEntry)
    );
    assert_eq!(
        dir.metadata_hash_at(PathFlags::empty(), "/etc/passwd".to_string())
            .await,
        Err(ErrorCode::NotPermitted)
    );
    assert_eq!(
        dir.metadata_hash_at(PathFlags::empty(), "/does-not-exist".to_string())
            .await,
        Err(ErrorCode::NotPermitted)
    );

    for (adir, aname, bdir, bname) in [
        (dir, ".", dir, "a.txt"),
        (dir, "a.txt", dir, "a.txt"),
        (dir, "a.txt", dir, "b.txt"),
        // https://github.com/bytecodealliance/wasmtime/issues/12172
        // (dir, "a.txt", &child, "symlink.cleanup"),
        (dir, "a.txt", &child, "link.cleanup"),
        // (dir, "b.txt", &child, "symlink.cleanup"),
        (dir, "a.txt", &child, "link.cleanup"),
    ] {
        check_metadata_hash_at(
            adir,
            PathFlags::empty(),
            aname,
            bdir,
            PathFlags::empty(),
            bname,
        )
        .await;
        check_metadata_hash_at(
            adir,
            PathFlags::empty(),
            aname,
            bdir,
            PathFlags::SYMLINK_FOLLOW,
            bname,
        )
        .await;
    }

    // https://github.com/bytecodealliance/wasmtime/issues/12172
    // child
    //     .unlink_file_at("symlink.cleanup".to_string())
    //     .await
    //     .unwrap();
    child
        .unlink_file_at("link.cleanup".to_string())
        .await
        .unwrap();
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
                test_metadata_hash(dir).await;
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
