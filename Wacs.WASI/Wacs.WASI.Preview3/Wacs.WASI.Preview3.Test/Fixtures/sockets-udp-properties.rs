use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{ErrorCode, IpAddressFamily, UdpSocket};

struct Component;
export!(Component);

fn test_properties(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();

    assert!(matches!(
        sock.set_unicast_hop_limit(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_unicast_hop_limit(1), Ok(_)));
    assert!(matches!(sock.set_unicast_hop_limit(u8::MAX), Ok(_)));

    assert!(matches!(
        sock.set_receive_buffer_size(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_receive_buffer_size(1), Ok(_)));
    assert!(matches!(sock.set_receive_buffer_size(u64::MAX), Ok(_)));
    assert!(matches!(
        sock.set_send_buffer_size(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_send_buffer_size(1), Ok(_)));
    assert!(matches!(sock.set_send_buffer_size(u64::MAX), Ok(_)));

    sock.set_unicast_hop_limit(42).unwrap();
    assert_eq!(sock.get_unicast_hop_limit().unwrap(), 42);

    sock.set_receive_buffer_size(0x10000).unwrap();
    assert_eq!(sock.get_receive_buffer_size().unwrap(), 0x10000);

    sock.set_send_buffer_size(0x10000).unwrap();
    assert_eq!(sock.get_send_buffer_size().unwrap(), 0x10000);
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_properties(IpAddressFamily::Ipv4);
        test_properties(IpAddressFamily::Ipv6);
        Ok(())
    }
}

fn main() {
    unreachable!()
}
