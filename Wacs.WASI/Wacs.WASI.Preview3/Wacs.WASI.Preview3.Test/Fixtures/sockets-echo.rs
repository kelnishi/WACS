wit_bindgen::generate!({
    inline: r"
	package wasi-testsuite:test;

	world test {
	    include wasi:sockets/imports@0.3.0-rc-2026-03-15;
	    include wasi:cli/command@0.3.0-rc-2026-03-15;
	}
    ",
    features:["clocks-timezone"],
    generate_all
});

use std::fmt;
use wasi::sockets::types::{
    IpAddressFamily, IpSocketAddress, Ipv4SocketAddress, Ipv6SocketAddress, TcpSocket,
};

use futures::join;
use wit_bindgen::StreamResult;

impl std::fmt::Display for IpSocketAddress {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match self {
            IpSocketAddress::Ipv4(Ipv4SocketAddress { port, address }) => {
                write!(
                    f,
                    "{}.{}.{}.{}:{}",
                    address.0, address.1, address.2, address.3, port
                )
            }
            IpSocketAddress::Ipv6(Ipv6SocketAddress { port, address, .. }) => {
                write!(
                    f,
                    "{}.{}.{}.{}.{}.{}.{}.{}:{}",
                    address.0,
                    address.1,
                    address.2,
                    address.3,
                    address.4,
                    address.5,
                    address.6,
                    address.7,
                    port
                )
            }
        }
    }
}

async fn echo(family: IpAddressFamily, addr: IpSocketAddress) {
    let listener = TcpSocket::create(family).unwrap();
    listener.bind(addr).unwrap();
    let addr = listener.get_local_address().unwrap();
    println!("{}", addr);

    let mut accept = listener.listen().unwrap();
    let sock = accept.next().await.unwrap();
    let (mut recv_stream, recv_fut) = sock.receive();

    // Read incoming data.
    // TODO(saul): Not incoming data fits in 100, this must be configurable.
    let (result, data) = recv_stream.read(Vec::with_capacity(100)).await;
    assert_eq!(result, StreamResult::Complete(data.len()));

    // Explicitly drop the stream, since we're not expecting more
    // incoming data.
    drop(recv_stream);
    recv_fut.await.unwrap();

    // Send the response
    let (mut send_tx, send_rx) = wit_stream::new();
    join!(
        async {
            sock.send(send_rx).await.unwrap();
        },
        async {
            let remaining = send_tx.write_all(data).await;
            assert!(remaining.is_empty());
            // Drop the stream, since we don't pretend to send more
            // data.
            drop(send_tx);
        }
    );
}

struct Component;
export!(Component);

impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        echo(
            IpAddressFamily::Ipv4,
            IpSocketAddress::Ipv4(Ipv4SocketAddress {
                port: 0,
                address: (127, 0, 0, 1),
            }),
        )
        .await;

        Ok(())
    }
}

fn main() {
    unreachable!()
}
