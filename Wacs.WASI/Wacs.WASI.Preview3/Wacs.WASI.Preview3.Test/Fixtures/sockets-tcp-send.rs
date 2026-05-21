use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::{
    self,
    wasi::sockets::types::{ErrorCode, IpAddressFamily, IpSocketAddress, TcpSocket},
};

struct Component;

export!(Component);

async fn test_connected_state(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();
    let (_, send_rx) = sockets::wit_stream::new();

    let result = sock.send(send_rx).await;
    assert!(matches!(result, Err(ErrorCode::InvalidState)));
}

// Dropping the write half shouldn't cause the write to be lost.
// The read half must remain functional.
// According to the spec, closing the stream is equivalent to
// `shutdown(SHUT_WR)` in POSIX.
async fn test_drop_write_half(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);
    let server = TcpSocket::create(family).unwrap();
    let client = TcpSocket::create(family).unwrap();
    let data = vec![0; 10];

    server.bind(addr).unwrap();
    let mut accept = server.listen().unwrap();
    let (mut send_tx, send_rx) = sockets::wit_stream::new();

    futures::join!(
        async {
            let sock = accept.next().await.unwrap();
            futures::join!(
                async {
                    sock.send(send_rx).await.unwrap();
                },
                async {
                    let remaining = send_tx.write_all(data.clone()).await;
                    assert!(remaining.is_empty());
                    drop(send_tx);
                },
                async {
                    let (sock_rx, sock_fut) = sock.receive();
                    let incoming_data = sock_rx.collect().await;
                    assert_eq!(data, incoming_data);
                    sock_fut.await.unwrap();
                }
            );
        },
        async {
            let local_addr = server.get_local_address().unwrap();
            client.connect(local_addr).await.unwrap();
            let (client_rx, client_fut) = client.receive();
            let incoming_data = client_rx.collect().await;
            assert_eq!(data, incoming_data);
            client_fut.await.unwrap();

            let (mut client_tx, client_rx) = sockets::wit_stream::new();
            futures::join!(
                async {
                    client.send(client_rx).await.unwrap();
                },
                async {
                    let remaining = client_tx.write_all(data.clone()).await;
                    assert!(remaining.is_empty());
                    drop(client_tx);
                },
            );
        }
    );
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_connected_state(IpAddressFamily::Ipv4).await;
        test_connected_state(IpAddressFamily::Ipv6).await;
        test_drop_write_half(IpAddressFamily::Ipv4).await;
        test_drop_write_half(IpAddressFamily::Ipv6).await;
        Ok(())
    }
}

fn main() {
    unreachable!()
}
