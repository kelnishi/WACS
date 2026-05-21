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

use wasi::clocks::system_clock::Instant;
use wasi::filesystem::types::Descriptor;
use wasi::filesystem::types::NewTimestamp;
use wasi::filesystem::types::{DescriptorFlags, ErrorCode, OpenFlags, PathFlags};
use wasi::filesystem::types::{DescriptorStat, DescriptorType};

fn check_timestamp(t: Instant) {
    assert!(t.nanoseconds < 1_000_000_000);
}

fn check_stat(stat: &DescriptorStat, type_: DescriptorType) {
    assert_eq!(stat.type_, type_);
    // assert_eq!(stat.link_count, 0) ?
    // assert_eq!(stat.size, 0) ?
    if let Some(t) = stat.data_access_timestamp {
        check_timestamp(t)
    };
    if let Some(t) = stat.data_modification_timestamp {
        check_timestamp(t)
    };
    if let Some(t) = stat.status_change_timestamp {
        check_timestamp(t)
    }
}

async fn test_stat(dir: &Descriptor) {
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
    let stat_with_flags = |flags: PathFlags, path: &str| dir.stat_at(flags, path.to_string());
    let stat = |path: &str| stat_with_flags(PathFlags::empty(), path);
    let stat_follow = |path: &str| stat_with_flags(PathFlags::SYMLINK_FOLLOW, path);

    // stat: async func() -> result<descriptor-stat, error-code>;
    check_stat(&dir.stat().await.unwrap(), DescriptorType::Directory);
    check_stat(&afd.stat().await.unwrap(), DescriptorType::RegularFile);
    check_stat(&bfd.stat().await.unwrap(), DescriptorType::RegularFile);

    // stat-at: async func(path-flags: path-flags, path: string) -> result<descriptor-stat, error-code>;
    assert_eq!(
        afd.stat_at(PathFlags::empty(), "z.txt".to_string()).await,
        Err(ErrorCode::NotDirectory)
    );
    assert_eq!(
        afd.stat_at(PathFlags::empty(), ".".to_string()).await,
        Err(ErrorCode::NotDirectory)
    );

    assert_eq!(stat("").await, Err(ErrorCode::NoEntry));
    assert_eq!(stat("..").await, Err(ErrorCode::NotPermitted));
    assert_eq!(stat_follow("parent").await, Err(ErrorCode::NotPermitted));
    assert_eq!(
        stat_follow("parent/fs-tests.dir").await,
        Err(ErrorCode::NotPermitted)
    );
    assert_eq!(stat(".").await, dir.stat().await);
    assert_eq!(stat("/").await, Err(ErrorCode::NotPermitted));
    assert_eq!(stat("/etc/passwd").await, Err(ErrorCode::NotPermitted));
    assert_eq!(stat("z.txt").await, Err(ErrorCode::NoEntry));

    // set-times-at: async func(path-flags: path-flags, path: string, data-access-timestamp: new-timestamp, data-modification-timestamp: new-timestamp) -> result<_, error-code>;
    // set-times: async func(data-access-timestamp: new-timestamp, data-modification-timestamp: new-timestamp) -> result<_, error-code>;
    let no_flags = PathFlags::empty();
    let follow_flag = PathFlags::SYMLINK_FOLLOW;
    let set_times_at = |flags, path: &str, atime, mtime| -> _ {
        dir.set_times_at(
            flags,
            path.to_string(),
            NewTimestamp::Timestamp(atime),
            NewTimestamp::Timestamp(mtime),
        )
    };
    {
        let atime = Instant {
            seconds: 42,
            nanoseconds: 0,
        };
        let mtime = Instant {
            seconds: 69,
            nanoseconds: 0,
        };
        assert_eq!(
            set_times_at(no_flags, "z.txt", atime, mtime).await,
            Err(ErrorCode::NoEntry)
        );
        assert_eq!(
            set_times_at(follow_flag, "", atime, mtime).await,
            Err(ErrorCode::NoEntry)
        );
        assert_eq!(
            set_times_at(no_flags, "/", atime, mtime).await,
            Err(ErrorCode::NotPermitted)
        );
        assert_eq!(
            set_times_at(no_flags, "..", atime, mtime).await,
            Err(ErrorCode::NotPermitted)
        );
        assert_eq!(
            set_times_at(follow_flag, "parent", atime, mtime).await,
            Err(ErrorCode::NotPermitted)
        );
        assert_eq!(
            set_times_at(no_flags, "../foo", atime, mtime).await,
            Err(ErrorCode::NotPermitted)
        );
        assert_eq!(
            set_times_at(no_flags, "parent/foo", atime, mtime).await,
            Err(ErrorCode::NotPermitted)
        );
    }

    if let Some(atime) = afd.stat().await.unwrap().data_access_timestamp {
        let mtime = afd
            .stat()
            .await
            .unwrap()
            .data_modification_timestamp
            .unwrap();
        let new_atime = Instant {
            seconds: 42,
            nanoseconds: 0,
        };
        let new_mtime = Instant {
            seconds: 69,
            nanoseconds: 0,
        };
        assert_eq!(
            set_times_at(no_flags, "a.txt", new_atime, new_mtime).await,
            Ok(())
        );
        assert_eq!(
            afd.stat().await.unwrap().data_access_timestamp,
            Some(new_atime)
        );
        assert_eq!(
            afd.stat().await.unwrap().data_modification_timestamp,
            Some(new_mtime)
        );
        assert_eq!(set_times_at(no_flags, "a.txt", atime, mtime).await, Ok(()));
        assert_eq!(afd.stat().await.unwrap().data_access_timestamp, Some(atime));
        assert_eq!(
            afd.stat().await.unwrap().data_modification_timestamp,
            Some(mtime)
        );

        assert_eq!(
            afd.set_times(
                NewTimestamp::Timestamp(new_atime),
                NewTimestamp::Timestamp(new_mtime)
            )
            .await,
            Ok(())
        );
        assert_eq!(
            afd.stat().await.unwrap().data_access_timestamp,
            Some(new_atime)
        );
        assert_eq!(
            afd.stat().await.unwrap().data_modification_timestamp,
            Some(new_mtime)
        );
        assert_eq!(
            afd.set_times(
                NewTimestamp::Timestamp(atime),
                NewTimestamp::Timestamp(mtime)
            )
            .await,
            Ok(())
        );
        assert_eq!(afd.stat().await.unwrap().data_access_timestamp, Some(atime));
        assert_eq!(
            afd.stat().await.unwrap().data_modification_timestamp,
            Some(mtime)
        );
    }
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_stat(dir).await;
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
