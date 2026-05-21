use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{
    ErrorCode, IpAddress, IpAddressFamily, IpSocketAddress, UdpSocket,
};

struct Component;

export!(Component);

fn test_invalid_address_family(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();

    let addr = match family {
        IpAddressFamily::Ipv4 => IpSocketAddress::localhost(IpAddressFamily::Ipv6, 0),
        IpAddressFamily::Ipv6 => IpSocketAddress::localhost(IpAddressFamily::Ipv4, 0),
    };

    let result = sock.bind(addr);
    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_already_bound(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, 0);
    assert!(sock.bind(addr).is_ok());
    assert!(matches!(sock.bind(addr), Err(ErrorCode::InvalidState)));
}

fn test_not_bindable(family: IpAddressFamily) {
    let mut non_bindable_addresses = Vec::new();

    match family {
        // https://datatracker.ietf.org/doc/html/rfc5737#section-3
        IpAddressFamily::Ipv4 => {
            non_bindable_addresses.push(IpAddress::Ipv4((192, 0, 2, 1)));
            non_bindable_addresses.push(IpAddress::Ipv4((198, 51, 100, 1)));
            non_bindable_addresses.push(IpAddress::Ipv4((203, 0, 113, 1)));
        }
        IpAddressFamily::Ipv6 => {
            non_bindable_addresses.push(IpAddress::Ipv6((0x2001, 0x0db8, 0, 0, 0, 0, 0, 1)));
        }
    };

    for addr in non_bindable_addresses {
        let sock = UdpSocket::create(family).unwrap();
        let socket_addr = IpSocketAddress::new(addr, 0);
        let result = sock.bind(socket_addr);

        assert_eq!(result, Err(ErrorCode::AddressNotBindable));
    }
}

fn test_ephemeral_port_assignment(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, 0);

    sock.bind(addr).unwrap();
    let bound = sock.get_local_address().unwrap();

    assert_eq!(addr.ip_addr(), bound.ip_addr());
    // Randomly assigned port.
    assert_ne!(addr.port(), bound.port());
}

fn test_reject_dual_stack() {
    let sock = UdpSocket::create(IpAddressFamily::Ipv6).unwrap();
    let addr = IpSocketAddress::ipv4_mapped_ipv6_localhost(0);
    let result = sock.bind(addr);

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_unspecified_addr(family: IpAddressFamily) {
    let addr = IpSocketAddress::unspecified(family, 0);
    let sock = UdpSocket::create(family).unwrap();
    sock.bind(addr).unwrap();
    let local_addr = sock.get_local_address().unwrap();
    assert_ne!(addr.port(), local_addr.port());
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_invalid_address_family(IpAddressFamily::Ipv4);
        test_invalid_address_family(IpAddressFamily::Ipv6);
        test_already_bound(IpAddressFamily::Ipv4);
        test_already_bound(IpAddressFamily::Ipv6);
        test_not_bindable(IpAddressFamily::Ipv4);
        test_not_bindable(IpAddressFamily::Ipv6);
        test_ephemeral_port_assignment(IpAddressFamily::Ipv4);
        test_ephemeral_port_assignment(IpAddressFamily::Ipv6);
        test_reject_dual_stack();
        test_unspecified_addr(IpAddressFamily::Ipv4);
        test_unspecified_addr(IpAddressFamily::Ipv6);

        Ok(())
    }
}

fn main() {
    unreachable!();
}
