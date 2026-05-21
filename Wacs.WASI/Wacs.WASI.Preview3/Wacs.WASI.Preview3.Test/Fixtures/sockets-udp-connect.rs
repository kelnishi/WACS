use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{
    ErrorCode, IpAddressFamily, IpSocketAddress, UdpSocket,
};

const PORT: u16 = 42;

struct Component;

export!(Component);

fn test_invalid_address_family(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();

    let addr = match family {
        IpAddressFamily::Ipv4 => IpSocketAddress::localhost(IpAddressFamily::Ipv6, 0),
        IpAddressFamily::Ipv6 => IpSocketAddress::localhost(IpAddressFamily::Ipv4, 0),
    };

    let result = sock.connect(addr);
    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_reject_dual_stack() {
    let sock = UdpSocket::create(IpAddressFamily::Ipv6).unwrap();
    let addr = IpSocketAddress::ipv4_mapped_ipv6_localhost(PORT);
    let result = sock.connect(addr);

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_unspecified_remote_addr(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::unspecified(family, PORT);
    let result = sock.connect(addr);

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_connect_0_port(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, 0);
    let result = sock.connect(addr);

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_implicit_bind(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();

    let addr = sock.get_local_address();
    assert!(addr.is_err());

    let addr = IpSocketAddress::localhost(family, PORT);
    let result = sock.connect(addr);

    assert!(result.is_ok());

    let local_addr = sock.get_local_address().unwrap();
    assert_eq!(addr.ip_addr(), local_addr.ip_addr());
    // Implicit bind assigns a random free port.
    assert_ne!(addr.port(), local_addr.port());
    let remote_addr = sock.get_remote_address().unwrap();
    assert_eq!(addr.ip_addr(), remote_addr.ip_addr());
    assert_eq!(addr.port(), remote_addr.port());
}

fn test_explicit_bind_addrinuse(family: IpAddressFamily) {
    let sock1 = UdpSocket::create(family).unwrap();
    sock1.bind(IpSocketAddress::localhost(family, 0)).unwrap();
    let sock1_addr = sock1.get_local_address().unwrap();

    let sock2 = UdpSocket::create(family).unwrap();
    let result = sock2.bind(sock1_addr);

    assert!(matches!(result, Err(ErrorCode::AddressInUse)));
}

fn test_reconnect_same_address(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, PORT);

    let result1 = sock.connect(addr);
    assert!(result1.is_ok());
    let addr1 = sock.get_remote_address().unwrap();

    let result2 = sock.connect(addr);
    assert!(result2.is_ok());
    let addr2 = sock.get_remote_address().unwrap();

    assert_eq!(addr1, addr2);
}

fn test_reconnect_different_address(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    assert!(matches!(sock.disconnect(), Err(ErrorCode::InvalidState)));

    let addr1 = IpSocketAddress::localhost(family, PORT);
    sock.connect(addr1).unwrap();
    assert_eq!(addr1, sock.get_remote_address().unwrap());

    assert!(sock.disconnect().is_ok());

    let addr2 = IpSocketAddress::localhost(family, PORT + 1);
    sock.connect(addr2).unwrap();
    assert_eq!(sock.get_remote_address().unwrap(), addr2);
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_invalid_address_family(IpAddressFamily::Ipv4);
        test_invalid_address_family(IpAddressFamily::Ipv6);

        test_reject_dual_stack();

        test_unspecified_remote_addr(IpAddressFamily::Ipv4);
        test_unspecified_remote_addr(IpAddressFamily::Ipv6);
        test_connect_0_port(IpAddressFamily::Ipv4);
        test_connect_0_port(IpAddressFamily::Ipv6);

        test_implicit_bind(IpAddressFamily::Ipv4);
        test_implicit_bind(IpAddressFamily::Ipv6);
        test_explicit_bind_addrinuse(IpAddressFamily::Ipv4);
        test_explicit_bind_addrinuse(IpAddressFamily::Ipv6);

        test_reconnect_same_address(IpAddressFamily::Ipv4);
        test_reconnect_same_address(IpAddressFamily::Ipv6);
        test_reconnect_different_address(IpAddressFamily::Ipv4);
        test_reconnect_different_address(IpAddressFamily::Ipv6);

        Ok(())
    }
}

fn main() {
    unreachable!()
}
