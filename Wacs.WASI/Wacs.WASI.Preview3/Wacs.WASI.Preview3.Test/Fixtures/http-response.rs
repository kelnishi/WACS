use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::http::{
    wasi::http::types::{Fields, HeaderError, Response},
    wit_future,
};

fn test_response_field_default_values(response: &Response) {
    assert_eq!(response.get_status_code(), 200);
}

fn test_status_codes(response: &Response) {
    for valid in [
        100, 101, 200, 201, 202, 203, 204, 205, 206, 300, 301, 302, 303, 304, 305, 306, 307, 308,
        400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 410, 411, 412, 413, 414, 415, 416, 417,
        421, 422, 426, 500, 501, 502, 503, 504, 505,
    ] {
        assert_eq!(response.set_status_code(valid as u16), Ok(()));
        assert_eq!(response.get_status_code(), valid);
    }
    for invalid in [0, 42, 600, 1000, 69, 65535] {
        response.set_status_code(invalid as u16).unwrap_err();
    }
}

fn test_immutable_headers(headers: &Fields) {
    assert_eq!(
        headers.append("Last-Modified", b"whatever"),
        Err(HeaderError::Immutable)
    );
}

fn test_headers_same(left: &Fields, right: &Fields) {
    assert_eq!(left.copy_all(), right.copy_all());
}

async fn test_response() -> Response {
    let headers = Fields::new();
    // No field-specific syntax checks.
    headers.append("content-type", b"!!!! invalid").unwrap();
    let contents = None;
    let (_, trailers_rx) = wit_future::new(|| Ok(None));
    let headers_copy = headers.clone();
    let (response, _sent_future) = Response::new(headers, contents, trailers_rx);

    test_response_field_default_values(&response);
    test_status_codes(&response);
    test_immutable_headers(&response.get_headers());
    test_headers_same(&response.get_headers(), &headers_copy);

    response
}

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_response().await;
        Ok(())
    }
}

fn main() {
    unreachable!("main is a stub");
}
