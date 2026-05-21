wit_bindgen::generate!({
    inline: r"
	package wasi-testsuite:test;

	world test {
	    include wasi:sockets/imports@0.3.0-rc-2026-03-15;
	    include wasi:cli/command@0.3.0-rc-2026-03-15;
	}
    ",
    features:["clocks-timezone"],
    additional_derives: [PartialEq, Eq],
    generate_all
});

use futures::join;
use wasi::sockets::types::{
    ErrorCode, IpAddress, IpAddressFamily, IpSocketAddress, Ipv4SocketAddress, Ipv6SocketAddress,
    TcpSocket,
};

struct Component;

export!(Component);

impl IpSocketAddress {
    fn ipv4_localhost(port: u16) -> IpSocketAddress {
        IpSocketAddress::Ipv4(Ipv4SocketAddress {
            port,
            address: (127, 0, 0, 1),
        })
    }

    fn ipv6_localhost(port: u16) -> IpSocketAddress {
        IpSocketAddress::Ipv6(Ipv6SocketAddress {
            port,
            address: (0, 0, 0, 0, 0, 0, 0, 1),
            flow_info: 0,
            scope_id: 0,
        })
    }

    fn ipv6_mapped_localhost(port: u16) -> IpSocketAddress {
        IpSocketAddress::Ipv6(Ipv6SocketAddress {
            port,
            address: (0, 0, 0, 0, 0, 0xFFFF, 0x7F00, 0x0001),
            flow_info: 0,
            scope_id: 0,
        })
    }

    fn localhost(family: IpAddressFamily, port: u16) -> IpSocketAddress {
        match family {
            IpAddressFamily::Ipv4 => Self::ipv4_localhost(port),
            IpAddressFamily::Ipv6 => Self::ipv6_localhost(port),
        }
    }

    fn new(addr: IpAddress, port: u16) -> IpSocketAddress {
        match addr {
            IpAddress::Ipv4(addr) => IpSocketAddress::Ipv4(Ipv4SocketAddress {
                port,
                address: addr,
            }),
            IpAddress::Ipv6(addr) => IpSocketAddress::Ipv6(Ipv6SocketAddress {
                port,
                address: addr,
                flow_info: 0,
                scope_id: 0,
            }),
        }
    }

    fn ip_addr(&self) -> IpAddress {
        match self {
            IpSocketAddress::Ipv6(addr) => IpAddress::Ipv6(addr.address),
            IpSocketAddress::Ipv4(addr) => IpAddress::Ipv4(addr.address),
        }
    }

    fn port(&self) -> u16 {
        match self {
            IpSocketAddress::Ipv6(addr) => addr.port,
            IpSocketAddress::Ipv4(addr) => addr.port,
        }
    }
}

fn test_invalid_address_family(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();

    let addr = match family {
        IpAddressFamily::Ipv4 => IpSocketAddress::localhost(IpAddressFamily::Ipv6, 0),
        IpAddressFamily::Ipv6 => IpSocketAddress::localhost(IpAddressFamily::Ipv4, 0),
    };

    let result = sock.bind(addr);
    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_ephemeral_port_assignment(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();
    let addr = IpSocketAddress::localhost(family, 0);

    sock.bind(addr).unwrap();
    let bound = sock.get_local_address().unwrap();

    assert_eq!(addr.ip_addr(), bound.ip_addr());
    // Randomly assigned port.
    assert_ne!(addr.port(), bound.port());
}

fn test_non_unicast(family: IpAddressFamily) {
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
        let socket_addr = IpSocketAddress::new(addr, 0);
        let result = sock.bind(socket_addr);

        assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
    }
}

fn test_reject_dual_stack() {
    let sock = TcpSocket::create(IpAddressFamily::Ipv6).unwrap();
    let addr = IpSocketAddress::ipv6_mapped_localhost(0);
    let result = sock.bind(addr);

    assert!(matches!(result, Err(ErrorCode::InvalidArgument)));
}

fn test_bind_addrinuse(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);

    let sock1 = TcpSocket::create(family).unwrap();
    sock1.bind(addr).unwrap();
    sock1.listen().unwrap();

    let bound_addr = sock1.get_local_address().unwrap();
    let sock2 = TcpSocket::create(family).unwrap();
    let result = sock2.bind(bound_addr);
    assert_eq!(result, Err(ErrorCode::AddressInUse));
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
        let sock = TcpSocket::create(family).unwrap();
        let socket_addr = IpSocketAddress::new(addr, 0);
        let result = sock.bind(socket_addr);

        assert_eq!(result, Err(ErrorCode::AddressNotBindable));
    }
}

fn test_already_bound(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);
    let sock = TcpSocket::create(family).unwrap();
    let result = sock.bind(addr);
    assert!(result.is_ok());
    let result = sock.bind(addr);
    assert_eq!(result, Err(ErrorCode::InvalidState));
}

async fn test_reuseaddr(family: IpAddressFamily) {
    let client = TcpSocket::create(family).unwrap();
    let local_addr = {
        let server = TcpSocket::create(family).unwrap();
        let addr = IpSocketAddress::localhost(family, 0);
        server.bind(addr).unwrap();
        let local_addr = server.get_local_address().unwrap();
        let mut accept = server.listen().unwrap();
        join!(
            // Change the state to connected.
            async {
                client.connect(local_addr).await.unwrap();
            },
            async {
                let sock = accept.next().await.unwrap();
                let (mut send_tx, send_rx) = wit_stream::new();
                join!(
                    async {
                        sock.send(send_rx).await.unwrap();
                    },
                    async {
                        let remaining = send_tx.write_all(vec![0; 1]).await;
                        assert!(remaining.is_empty());
                        drop(send_tx);
                    }
                );
            }
        );
        local_addr
    };

    // Immediately try to connect to the same after the connection is
    // dropped.  According to the spec, `SO_REUSEADDR` should be set
    // by default, so the next connection should not be affected by
    // the `TIME_WAIT` state.
    let next = TcpSocket::create(family).unwrap();
    next.bind(local_addr).unwrap();
    next.listen().unwrap();
}

impl exports::wasi::cli::run::Guest for Component {
    async fn run() -> Result<(), ()> {
        test_invalid_address_family(IpAddressFamily::Ipv4);
        test_invalid_address_family(IpAddressFamily::Ipv6);
        test_ephemeral_port_assignment(IpAddressFamily::Ipv4);
        test_ephemeral_port_assignment(IpAddressFamily::Ipv6);
        test_non_unicast(IpAddressFamily::Ipv4);
        test_non_unicast(IpAddressFamily::Ipv6);
        test_reject_dual_stack();
        test_bind_addrinuse(IpAddressFamily::Ipv4);
        test_bind_addrinuse(IpAddressFamily::Ipv6);
        test_not_bindable(IpAddressFamily::Ipv4);
        test_not_bindable(IpAddressFamily::Ipv6);
        test_already_bound(IpAddressFamily::Ipv4);
        test_already_bound(IpAddressFamily::Ipv6);
        test_reuseaddr(IpAddressFamily::Ipv4).await;
        test_reuseaddr(IpAddressFamily::Ipv6).await;
        Ok(())
    }
}

fn main() {
    unreachable!()
}
