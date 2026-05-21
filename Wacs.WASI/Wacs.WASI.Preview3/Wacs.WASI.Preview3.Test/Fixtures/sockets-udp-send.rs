use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{
    ErrorCode, IpAddressFamily, IpSocketAddress, UdpSocket,
};

struct Component;

const PORT: u16 = 42;

export!(Component);

async fn test_wrong_address_family(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();

    let addr = match family {
        IpAddressFamily::Ipv4 => IpSocketAddress::localhost(IpAddressFamily::Ipv6, 0),
        IpAddressFamily::Ipv6 => IpSocketAddress::localhost(IpAddressFamily::Ipv4, 0),
    };

    let result = sock.send(vec![0; 1], Some(addr)).await;
    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

async fn test_implicit_bind(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let remote_addr = IpSocketAddress::localhost(family, PORT);

    assert!(sock.send(vec![0; 1], Some(remote_addr)).await.is_ok());
    assert!(sock.get_local_address().is_ok());
    assert!(sock.get_remote_address().is_err());
}

async fn test_connected_empty_addr(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    sock.connect(IpSocketAddress::localhost(family, PORT))
        .unwrap();

    assert!(sock.send(vec![0; 1], None).await.is_ok());
}

async fn test_connected_with_addr(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let remote_addr = IpSocketAddress::localhost(family, PORT);
    sock.connect(remote_addr).unwrap();

    matches!(
        sock.send(
            vec![0; 1],
            Some(IpSocketAddress::localhost(family, PORT + 1))
        )
        .await,
        Err(ErrorCode::InvalidArgument)
    );

    sock.send(vec![0; 1], Some(remote_addr)).await.unwrap();
}

async fn test_not_connected_empty_addr(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    assert!(matches!(
        sock.send(vec![0; 1], None).await,
        Err(ErrorCode::InvalidArgument)
    ));
}

async fn test_unspecified_remote_addr(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let unspec = IpSocketAddress::unspecified(family, PORT);
    let result = sock.send(vec![0; 1], Some(unspec)).await;
    // FIXME: According to the spec this should return
    //     `invalid-argument`: The IP address in `remote-address` is
    //     set to INADDR_ANY (`0.0.0.0` / `::`).
    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

async fn test_remote_addr_with_port_0(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, 0);
    let result = sock.send(vec![0; 1], Some(addr)).await;
    assert!(matches!(result, Err(ErrorCode::AddressNotBindable)));
}

async fn test_datagram_too_large(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, PORT);
    let result = sock.send(vec![0u8; 65536], Some(addr)).await;

    assert!(matches!(result, Err(ErrorCode::DatagramTooLarge)));
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_wrong_address_family(IpAddressFamily::Ipv4).await;
        test_wrong_address_family(IpAddressFamily::Ipv6).await;
        test_unspecified_remote_addr(IpAddressFamily::Ipv4).await;
        test_unspecified_remote_addr(IpAddressFamily::Ipv6).await;
        test_remote_addr_with_port_0(IpAddressFamily::Ipv4).await;
        test_remote_addr_with_port_0(IpAddressFamily::Ipv6).await;
        test_implicit_bind(IpAddressFamily::Ipv4).await;
        test_implicit_bind(IpAddressFamily::Ipv6).await;
        test_connected_empty_addr(IpAddressFamily::Ipv4).await;
        test_connected_empty_addr(IpAddressFamily::Ipv6).await;
        test_connected_with_addr(IpAddressFamily::Ipv4).await;
        test_connected_with_addr(IpAddressFamily::Ipv6).await;
        test_not_connected_empty_addr(IpAddressFamily::Ipv4).await;
        test_not_connected_empty_addr(IpAddressFamily::Ipv6).await;
        test_datagram_too_large(IpAddressFamily::Ipv4).await;
        test_datagram_too_large(IpAddressFamily::Ipv6).await;
        Ok(())
    }
}

fn main() {
    unreachable!()
}
