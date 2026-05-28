use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::sockets::wasi::sockets::types::{
    ErrorCode, IpAddressFamily, IpSocketAddress, UdpSocket,
};

struct Component;

export!(Component);

async fn test_not_bound(family: IpAddressFamily) {
    let sock = UdpSocket::create(family).unwrap();
    assert!(matches!(sock.receive().await, Err(ErrorCode::InvalidState)));
}

async fn test_receive_data(family: IpAddressFamily) {
    let server = UdpSocket::create(family).unwrap();
    let client = UdpSocket::create(family).unwrap();

    let server_addr = IpSocketAddress::localhost(family, 0);
    server.bind(server_addr).unwrap();
    let server_addr = server.get_local_address().unwrap();
    client.connect(server_addr).unwrap();

    futures::join!(
        async {
            let (data, sender) = server.receive().await.unwrap();
            assert_eq!(data, vec![1, 2, 3, 4]);
            assert_eq!(sender, client.get_local_address().unwrap());
        },
        async {
            client.send(vec![1, 2, 3, 4], None).await.unwrap();
        }
    );
}

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_not_bound(IpAddressFamily::Ipv4).await;
        test_not_bound(IpAddressFamily::Ipv6).await;
        test_receive_data(IpAddressFamily::Ipv4).await;
        test_receive_data(IpAddressFamily::Ipv6).await;
        Ok(())
    }
}

fn main() {
    unreachable!()
}
