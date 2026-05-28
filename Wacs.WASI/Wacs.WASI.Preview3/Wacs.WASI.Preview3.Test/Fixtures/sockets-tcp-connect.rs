use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{
    ErrorCode, IpAddress, IpAddressFamily, IpSocketAddress, TcpSocket,
};

const PORT: u16 = 42;

struct Component;

export!(Component);

async fn test_invalid_address_family(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();

    let addr = match family {
        IpAddressFamily::Ipv4 => IpSocketAddress::localhost(IpAddressFamily::Ipv6, PORT),
        IpAddressFamily::Ipv6 => IpSocketAddress::localhost(IpAddressFamily::Ipv4, PORT),
    };

    let result = sock.connect(addr).await;
    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

async fn test_non_unicast(family: IpAddressFamily) {
    let mut non_unicast_addresses = Vec::new();

    match family {
        IpAddressFamily::Ipv4 => {
            // Multicast
            for nibble in 224..=239 {
                non_unicast_addresses.push(IpAddress::Ipv4((nibble, 0, 0, 1)));
            }
            // Limited broadcast
            non_unicast_addresses.push(IpAddress::Ipv4((255, 255, 255, 255)));
        }
        IpAddressFamily::Ipv6 => {
            // Multicast
            for b in 0xff00..=0xffff {
                non_unicast_addresses.push(IpAddress::Ipv6((b, 0, 0, 0, 0, 0, 0, 1)));
            }
        }
    };

    for addr in non_unicast_addresses {
        let sock = TcpSocket::create(family).unwrap();
        let socket_addr = IpSocketAddress::new(addr, PORT);
        let result = sock.connect(socket_addr).await;

        assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
    }
}

async fn test_reject_dual_stack() {
    let sock = TcpSocket::create(IpAddressFamily::Ipv6).unwrap();
    let addr = IpSocketAddress::ipv4_mapped_ipv6_localhost(PORT);
    let result = sock.connect(addr).await;

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

async fn test_unspecified_remote_addr(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();
    let addr = IpSocketAddress::unspecified(family, PORT);
    let result = sock.connect(addr).await;

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

async fn test_connect_0_port(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, 0);
    let result = sock.connect(addr).await;

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

async fn test_connected_state(family: IpAddressFamily) {
    let listener = TcpSocket::create(family).unwrap();
    listener
        .bind(IpSocketAddress::localhost(family, 0))
        .unwrap();
    let server_addr = listener.get_local_address().unwrap();
    listener.listen().unwrap();

    let sock = TcpSocket::create(family).unwrap();
    let result = sock.connect(server_addr).await;
    assert!(result.is_ok());

    let result = sock.connect(server_addr).await;
    assert!(matches!(result, Err(ErrorCode::InvalidState)));
}

async fn test_listening_state(family: IpAddressFamily) {
    let listener = TcpSocket::create(family).unwrap();
    listener
        .bind(IpSocketAddress::localhost(family, 0))
        .unwrap();
    listener.listen().unwrap();

    let result = listener
        .connect(IpSocketAddress::localhost(family, PORT))
        .await;
    assert!(matches!(result, Err(ErrorCode::InvalidState)));
}

async fn test_connection_refused(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();
    sock.bind(IpSocketAddress::localhost(family, 0)).unwrap();
    let addr = sock.get_local_address().unwrap();
    drop(sock);

    let sock2 = TcpSocket::create(family).unwrap();
    let result = sock2.connect(addr).await;

    assert!(matches!(result, Err(ErrorCode::ConnectionRefused)));
}

async fn test_explicit_bind(family: IpAddressFamily) {
    let (listener, mut accept) = {
        let bind_address = IpSocketAddress::localhost(family, 0);
        let listener = TcpSocket::create(family).unwrap();
        listener.bind(bind_address).unwrap();
        let accept = listener.listen().unwrap();
        (listener, accept)
    };

    let listener_address = listener.get_local_address().unwrap();
    let client = TcpSocket::create(family).unwrap();

    client.bind(IpSocketAddress::localhost(family, 0)).unwrap();

    futures::join!(
        async {
            client.connect(listener_address).await.unwrap();
        },
        async {
            accept.next().await.unwrap();
        }
    );
}

async fn test_explicit_bind_addrinuse(family: IpAddressFamily) {
    let listener = {
        let bind_address = IpSocketAddress::localhost(family, 0);
        let listener = TcpSocket::create(family).unwrap();
        listener.bind(bind_address).unwrap();
        listener
    };

    let listener_address = listener.get_local_address().unwrap();
    let client = TcpSocket::create(family).unwrap();

    let result = client.bind(listener_address);
    assert!(matches!(result, Err(ErrorCode::AddressInUse)));
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_invalid_address_family(IpAddressFamily::Ipv4).await;
        test_invalid_address_family(IpAddressFamily::Ipv6).await;
        test_non_unicast(IpAddressFamily::Ipv4).await;
        test_non_unicast(IpAddressFamily::Ipv6).await;
        test_reject_dual_stack().await;
        test_unspecified_remote_addr(IpAddressFamily::Ipv4).await;
        test_unspecified_remote_addr(IpAddressFamily::Ipv6).await;
        test_connect_0_port(IpAddressFamily::Ipv4).await;
        test_connect_0_port(IpAddressFamily::Ipv6).await;
        test_connection_refused(IpAddressFamily::Ipv4).await;
        test_connection_refused(IpAddressFamily::Ipv6).await;
        test_connected_state(IpAddressFamily::Ipv4).await;
        test_connected_state(IpAddressFamily::Ipv6).await;
        test_listening_state(IpAddressFamily::Ipv4).await;
        test_listening_state(IpAddressFamily::Ipv6).await;
        test_explicit_bind(IpAddressFamily::Ipv4).await;
        test_explicit_bind(IpAddressFamily::Ipv6).await;
        test_explicit_bind_addrinuse(IpAddressFamily::Ipv4).await;
        test_explicit_bind_addrinuse(IpAddressFamily::Ipv6).await;
        Ok(())
    }
}

fn main() {
    unreachable!()
}
