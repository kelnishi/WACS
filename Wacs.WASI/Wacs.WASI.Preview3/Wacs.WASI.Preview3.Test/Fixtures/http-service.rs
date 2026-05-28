use test_wasm32_wasip3::http::{export, exports::wasi::http::handler::Guest};
use test_wasm32_wasip3::http::{
    wasi::http::types::{ErrorCode, Fields, Method, Request, Response, StatusCode},
    wit_future, wit_stream,
};

fn handle_root(headers: &Fields) -> (StatusCode, Option<Vec<u8>>) {
    headers.append("content-type", b"text/plain").unwrap();
    (200, Some(b"hey\n".to_vec()))
}

fn handle_not_found(_headers: &Fields) -> (StatusCode, Option<Vec<u8>>) {
    (404, None)
}

fn handle_method_not_allowed(_headers: &Fields) -> (StatusCode, Option<Vec<u8>>) {
    (405, None)
}

struct Component;
export!(Component);

impl Guest for Component {
    async fn handle(request: Request) -> Result<Response, ErrorCode> {
        let headers = Fields::new();
        let (status, payload) = match (request.get_method(), request.get_path_with_query()) {
            (Method::Get, Some(s)) if s == "/" => handle_root(&headers),
            (Method::Get, _) => handle_not_found(&headers),
            (_, _) => handle_method_not_allowed(&headers),
        };
        let (_, result_rx) = wit_future::new(|| Ok(()));
        let (body_rx, trailers) = Request::consume_body(request, result_rx);
        assert!(body_rx.collect().await.is_empty());
        assert!(trailers.await.unwrap().is_none());

        let (trailers_tx, trailers_rx) = wit_future::new(|| Ok(None));
        drop(trailers_tx);
        let response_body = payload.map(|bytes| {
            headers
                .append("content-length", &bytes.len().to_string().into_bytes())
                .unwrap();
            let (mut body_tx, body_rx) = wit_stream::new();
            wit_bindgen::spawn(async move {
                let remaining = body_tx.write_all(bytes).await;
                assert!(remaining.is_empty());
                drop(body_tx);
            });
            body_rx
        });
        let (response, _sent) = Response::new(headers, response_body, trailers_rx);
        response.set_status_code(status).unwrap();
        Ok(response)
    }
}

fn main() {
    unreachable!("main is a stub");
}
