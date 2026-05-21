use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{
    ErrorCode, IpAddressFamily, IpSocketAddress, TcpSocket,
};

struct Component;

export!(Component);

fn test_with_bind(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);
    let sock = TcpSocket::create(family).unwrap();
    sock.bind(addr).unwrap();
    assert!(sock.listen().is_ok());
}

fn test_without_bind(family: IpAddressFamily) {
    // Without an explicit bind, `listen` will assign an ephemeral
    // port.
    let sock = TcpSocket::create(family).unwrap();
    assert!(sock.get_local_address().is_err());
    assert!(sock.listen().is_ok());
    assert!(sock.get_local_address().is_ok());
}

async fn test_inherited_properties(family: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(family, 0);
    let sock = TcpSocket::create(family).unwrap();
    sock.bind(addr).unwrap();
    let client = TcpSocket::create(family).unwrap();
    let local_addr = sock.get_local_address().unwrap();

    let mut accept = sock.listen().unwrap();
    futures::join!(
        async {
            let next = accept.next().await.unwrap();
            assert_eq!(next.get_address_family(), sock.get_address_family());
            assert_eq!(next.get_keep_alive_enabled(), sock.get_keep_alive_enabled());
            assert_eq!(
                next.get_keep_alive_idle_time(),
                sock.get_keep_alive_idle_time()
            );
            assert_eq!(
                next.get_keep_alive_interval(),
                sock.get_keep_alive_interval()
            );
            assert_eq!(next.get_keep_alive_count(), sock.get_keep_alive_count());
            assert_eq!(next.get_hop_limit(), sock.get_hop_limit());
            assert_eq!(
                next.get_receive_buffer_size(),
                sock.get_receive_buffer_size()
            );
            assert_eq!(next.get_send_buffer_size(), sock.get_send_buffer_size());
        },
        async {
            client.connect(local_addr).await.unwrap();
        }
    );
}

fn test_listening(fam: IpAddressFamily) {
    let addr = IpSocketAddress::localhost(fam, 0);
    let sock = TcpSocket::create(fam).unwrap();
    sock.bind(addr).unwrap();
    sock.listen().unwrap();
    let result = sock.listen();
    assert!(matches!(result, Err(ErrorCode::InvalidState)));
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_with_bind(IpAddressFamily::Ipv4);
        test_with_bind(IpAddressFamily::Ipv6);
        test_without_bind(IpAddressFamily::Ipv4);
        test_without_bind(IpAddressFamily::Ipv6);
        test_inherited_properties(IpAddressFamily::Ipv4).await;
        test_inherited_properties(IpAddressFamily::Ipv6).await;
        test_listening(IpAddressFamily::Ipv4);
        test_listening(IpAddressFamily::Ipv6);
        Ok(())
    }
}

fn main() {
    unreachable!()
}
