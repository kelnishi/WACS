use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{ErrorCode, IpAddressFamily, TcpSocket};

struct Component;

export!(Component);

const SECOND: u64 = 1_000_000_000;

fn test_properties(family: IpAddressFamily) {
    let sock = TcpSocket::create(family).unwrap();

    assert!(matches!(
        sock.set_listen_backlog_size(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_listen_backlog_size(1), Ok(_)));
    assert!(matches!(sock.set_listen_backlog_size(u64::MAX), Ok(_)));

    assert!(matches!(sock.set_keep_alive_enabled(true), Ok(_)));
    assert!(matches!(sock.set_keep_alive_enabled(false), Ok(_)));

    assert!(matches!(
        sock.set_keep_alive_idle_time(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_keep_alive_idle_time(1), Ok(_)));
    let idle_time = sock.get_keep_alive_idle_time().unwrap();
    assert!(idle_time > 0 && idle_time <= 1 * SECOND);
    assert!(matches!(sock.set_keep_alive_idle_time(u64::MAX), Ok(_)));

    assert!(matches!(
        sock.set_keep_alive_interval(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_keep_alive_interval(1), Ok(_)));
    let idle_time = sock.get_keep_alive_interval().unwrap();
    assert!(idle_time > 0 && idle_time <= 1 * SECOND);
    assert!(matches!(sock.set_keep_alive_interval(u64::MAX), Ok(_)));

    assert!(matches!(
        sock.set_keep_alive_count(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_keep_alive_count(1), Ok(_)));
    assert!(matches!(sock.set_keep_alive_count(u32::MAX), Ok(_)));

    assert!(matches!(
        sock.set_hop_limit(0),
        Err(ErrorCode::InvalidArgument)
    ));
    assert!(matches!(sock.set_hop_limit(1), Ok(_)));
    assert!(matches!(sock.set_hop_limit(u8::MAX), Ok(_)));

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

    sock.set_keep_alive_enabled(true).unwrap();
    assert_eq!(sock.get_keep_alive_enabled().unwrap(), true);
    sock.set_keep_alive_enabled(false).unwrap();
    assert_eq!(sock.get_keep_alive_enabled().unwrap(), false);

    sock.set_keep_alive_idle_time(42 * SECOND).unwrap();
    assert_eq!(sock.get_keep_alive_idle_time().unwrap(), 42 * SECOND);

    sock.set_keep_alive_interval(42 * SECOND).unwrap();
    assert_eq!(sock.get_keep_alive_interval().unwrap(), 42 * SECOND);

    sock.set_keep_alive_count(42).unwrap();
    assert_eq!(sock.get_keep_alive_count().unwrap(), 42);

    sock.set_hop_limit(42).unwrap();
    assert_eq!(sock.get_hop_limit().unwrap(), 42);

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
