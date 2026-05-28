use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::{
    self,
    wasi::sockets::types::{ErrorCode, IpAddressFamily, IpSocketAddress, TcpSocket},
};

struct Component;

export!(Component);

async fn test_connected_state(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();

    let (_, fut) = sock.receive();
    assert!(matches!(fut.await, Err(ErrorCode::InvalidState)));
}

async fn test_multiple_receive(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);
    let server = TcpSocket::create(family).unwrap();
    let client = TcpSocket::create(family).unwrap();
    server.bind(addr).unwrap();

    let mut accept = server.listen().unwrap();

    futures::join!(
        async {
            let sock = accept.next().await.unwrap();
            let (_, fut1) = sock.receive();
            let (_, fut2) = sock.receive();
            assert!(fut1.await.is_ok());
            assert!(matches!(fut2.await, Err(ErrorCode::InvalidState)));
        },
        async {
            let local_addr = server.get_local_address().unwrap();
            client.connect(local_addr).await.unwrap();
        }
    );
}

// According to the spec, dropping the read half behaves like calling
// `shutdown(SHUT_RD)` in POSIX.
async fn test_drop_read_half(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);
    let server = TcpSocket::create(family).unwrap();
    let client = TcpSocket::create(family).unwrap();
    server.bind(addr).unwrap();

    let mut accept = server.listen().unwrap();

    let (sock, ()) = futures::join!(async { accept.next().await.unwrap() }, async {
        client
            .connect(server.get_local_address().unwrap())
            .await
            .unwrap();
    });

    assert!(write_all(&sock, vec![0; 1]).await.is_ok());

    let (client_rx, client_fut) = client.receive();
    drop(client_rx);

    // Not asserting an specific error here since the spec only states
    // that writing to a closed socket returns a regular error:
    // https://github.com/WebAssembly/WASI/blob/main/proposals/sockets/Posix-compatibility.md#writing-to-closed-streams-sigpipe-so_nosigpipe-
    assert!(write_all(&sock, vec![0; 1]).await.is_err());

    client_fut.await.unwrap();
}

async fn write_all(sock: &TcpSocket, data: Vec<u8>) -> Result<(), ErrorCode> {
    let (mut sock_tx, sock_rx) = sockets::wit_stream::new();
    let (r, ()) = futures::join!(async { sock.send(sock_rx).await }, async {
        sock_tx.write_all(data).await;
        drop(sock_tx);
    });
    r
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_connected_state(IpAddressFamily::Ipv4).await;
        test_connected_state(IpAddressFamily::Ipv6).await;
        test_multiple_receive(IpAddressFamily::Ipv4).await;
        test_multiple_receive(IpAddressFamily::Ipv6).await;
        test_drop_read_half(IpAddressFamily::Ipv4).await;
        test_drop_read_half(IpAddressFamily::Ipv6).await;
        Ok(())
    }
}

fn main() {
    unreachable!()
}
