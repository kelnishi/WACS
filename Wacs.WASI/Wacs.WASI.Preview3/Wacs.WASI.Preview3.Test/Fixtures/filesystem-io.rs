use futures::join;
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
use wit_bindgen::StreamResult;

async fn pread(fd: &Descriptor, size: usize, offset: u64) -> Result<Vec<u8>, ErrorCode> {
    let (mut rx, future) = fd.read_via_stream(offset);
    let data = Vec::<u8>::with_capacity(size);
    let mut bytes_read = 0;
    let (mut result, mut data) = rx.read(data).await;
    loop {
        match result {
            StreamResult::Complete(n) => {
                assert!(n <= size - bytes_read);
                bytes_read += n;
                assert_eq!(data.len(), bytes_read);
                if bytes_read == size {
                    break;
                }
                (result, data) = rx.read(data).await;
            }
            StreamResult::Dropped => {
                // https://github.com/bytecodealliance/wit-bindgen/issues/1396
                assert!(data.len() >= bytes_read);
                break;
            }
            StreamResult::Cancelled => {
                panic!("who cancelled the stream?");
            }
        }
    }
    drop(rx);
    match future.await {
        Ok(()) => Ok(data),
        Err(err) => Err(err),
    }
}

async fn pwrite(fd: &Descriptor, offset: u64, data: &[u8]) -> Result<usize, ErrorCode> {
    let (mut tx, rx) = wit_stream::new();
    let future = fd.write_via_stream(rx, offset);
    let len = data.len();
    let mut written: usize = 0;
    let mut result: Result<(), ErrorCode> = Ok(());
    join! {
        async {
            let (mut result, mut buf) = tx.write(data.to_vec()).await;
            loop {
                match result {
                    StreamResult::Complete(n) => {
                        assert!(n <= len - written);
                        written += n;
                        assert_eq!(buf.remaining(), len - written);
                        if buf.remaining() != 0 {
                            (result, buf) = tx.write_buf(buf).await;
                        } else {
                            break;
                        }
                    }
                    StreamResult::Dropped => {
                        // https://github.com/bytecodealliance/wit-bindgen/issues/1396
                        assert!(buf.remaining() <= len - written);
                        panic!("receiver dropped the stream?");
                    }
                    StreamResult::Cancelled => {
                        break;
                    }
                }
            }
            assert_eq!(buf.remaining(), len - written);
            drop(tx);
        },
        async { result = future.await; }
    };
    match result {
        Ok(()) => Ok(written),
        Err(err) => Err(err),
    }
}

async fn pappend(fd: &Descriptor, data: &[u8]) -> Result<usize, ErrorCode> {
    let (mut tx, rx) = wit_stream::new();
    let future = fd.append_via_stream(rx);
    let initial_size = fd.stat().await.unwrap().size as usize;
    let len = data.len();
    let mut written: usize = 0;
    let mut result: Result<(), ErrorCode> = Ok(());
    join! {
        async {
            let (mut result, mut buf) = tx.write(data.to_vec()).await;
            loop {
                match result {
                    StreamResult::Complete(n) => {
                        assert!(n <= len - written);
                        written += n;
                        assert_eq!(buf.remaining(), len - written);
                        assert_eq!(fd.stat().await.unwrap().size as usize,
                                   initial_size + written);
                        if buf.remaining() != 0 {
                            (result, buf) = tx.write_buf(buf).await;
                        } else {
                            break;
                        }
                    }
                    StreamResult::Dropped => {
                        panic!("receiver dropped the stream?");
                    }
                    StreamResult::Cancelled => {
                        break;
                    }
                }
            }
            assert_eq!(buf.remaining(), len - written);
            drop(tx);
        },
        async { result = future.await; }
    };
    match result {
        Ok(()) => Ok(written),
        Err(err) => Err(err),
    }
}

async fn read_to_eof(fd: &Descriptor, offset: u64) -> Vec<u8> {
    let (stream, success) = fd.read_via_stream(offset);
    let ret = stream.collect().await;
    success.await.unwrap();
    ret
}

async fn test_io(dir: &Descriptor) {
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

    let a = open_r("a.txt").await.unwrap();

    pread(&a, 0, 0).await.unwrap();
    pread(&a, 0, 1).await.unwrap();
    pread(&a, 0, 6).await.unwrap();
    pread(&a, 0, 7).await.unwrap();
    pread(&a, 0, 17).await.unwrap();

    assert_eq!(&pread(&a, 1, 0).await.unwrap(), b"t");
    assert_eq!(&pread(&a, 1, 1).await.unwrap(), b"e");
    assert_eq!(&pread(&a, 1, 6).await.unwrap(), b"\n");
    assert_eq!(&pread(&a, 1, 7).await.unwrap(), b"");
    assert_eq!(&pread(&a, 1, 17).await.unwrap(), b"");

    assert_eq!(&read_to_eof(&a, 0).await, b"test-a\n");
    assert_eq!(&read_to_eof(&a, 1).await, b"est-a\n");
    assert_eq!(&read_to_eof(&a, 6).await, b"\n");
    assert_eq!(&read_to_eof(&a, 7).await, b"");
    assert_eq!(&read_to_eof(&a, 17).await, b"");

    // No-op on read-only fds.
    a.sync_data().await.unwrap();
    a.sync().await.unwrap();

    assert_eq!(pread(&a, 1, u64::MAX).await, Err(ErrorCode::Invalid));

    let c = creat("c.cleanup").await.unwrap();
    assert_eq!(&read_to_eof(&c, 0).await, b"");
    assert_eq!(pwrite(&c, 0, b"hello!").await, Ok(b"hello!".len()));
    assert_eq!(&read_to_eof(&c, 0).await, b"hello!");
    assert_eq!(pwrite(&c, 0, b"byeee").await, Ok(b"byeee".len()));
    assert_eq!(&read_to_eof(&c, 0).await, b"byeee!");
    assert_eq!(pappend(&c, b" laters!!").await, Ok(b" laters!!".len()));
    assert_eq!(&read_to_eof(&c, 0).await, b"byeee! laters!!");
    c.sync_data().await.unwrap();
    assert_eq!(
        &read_to_eof(&open_r("c.cleanup").await.unwrap(), 0).await,
        b"byeee! laters!!"
    );
    c.sync().await.unwrap();

    rm("c.cleanup").await.unwrap();
}

struct Component;
export!(Component);
impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        match &wasi::filesystem::preopens::get_directories()[..] {
            [(dir, dirname)] if dirname == "fs-tests.dir" => {
                test_io(dir).await;
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
