use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::http::{
    wasi::http::types::{Fields, HeaderError, Method, Request, Scheme},
    wit_future,
};

fn test_request_field_default_values(request: &Request) {
    assert_eq!(request.get_method(), Method::Get);
    assert_eq!(request.get_path_with_query(), None);
    assert_eq!(request.get_scheme(), None);
    assert_eq!(request.get_authority(), None);
    assert!(request.get_options().is_none());
}

fn compute_valid_method_chars(len: usize) -> Vec<bool> {
    // https://www.rfc-editor.org/rfc/rfc9110.html#section-5.6.2
    //  field-name     = token
    //  token          = 1*tchar
    //
    //  tchar          = "!" / "#" / "$" / "%" / "&" / "'" / "*"
    //                 / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~"
    //                 / DIGIT / ALPHA
    //                 ; any VCHAR, except delimiters
    let mut ret = Vec::<bool>::new();
    ret.resize(len, false);
    for ch in "#$%&'!*+-.^_`|~".chars() {
        ret[ch as usize] = true;
    }
    for ch in 'a'..='z' {
        ret[ch as usize] = true;
    }
    for ch in 'A'..='Z' {
        ret[ch as usize] = true;
    }
    for ch in '0'..='9' {
        ret[ch as usize] = true;
    }
    ret
}

fn test_method_names(request: &Request) {
    for (m, name) in [
        (Method::Get, "GET"),
        (Method::Head, "HEAD"),
        (Method::Post, "POST"),
        (Method::Put, "PUT"),
        (Method::Delete, "DELETE"),
        (Method::Connect, "CONNECT"),
        (Method::Options, "OPTIONS"),
        (Method::Trace, "TRACE"),
        (Method::Patch, "PATCH"),
    ] {
        assert_eq!(request.set_method(&m), Ok(()));
        assert_eq!(request.get_method(), m);
        // https://github.com/WebAssembly/wasi-http/issues/194
        assert_eq!(request.set_method(&Method::Other(name.to_string())), Ok(()));
        assert_eq!(request.get_method(), m);
    }

    request
        .set_method(&Method::Other("coucou".to_string()))
        .unwrap();
    assert_eq!(request.get_method(), Method::Other("coucou".to_string()));

    request
        .set_method(&Method::Other("".to_string()))
        .unwrap_err();
    assert_eq!(request.get_method(), Method::Other("coucou".to_string()));

    let max_codepoint_to_test = 1024;
    let valid = compute_valid_method_chars(max_codepoint_to_test);
    for ch in 0..max_codepoint_to_test {
        let method_name = String::from(char::from_u32(ch as u32).unwrap());
        if "#$%&'".contains(&method_name) {
            // https://github.com/bytecodealliance/wasmtime/issues/11772
            continue;
        }
        let method = Method::Other(method_name);
        if valid[ch] {
            assert_eq!(request.set_method(&method), Ok(()));
            assert_eq!(request.get_method(), method);
        } else {
            assert_eq!(request.set_method(&method), Err(()));
        }
    }
}

fn test_schemes(request: &Request) {
    for s in [Scheme::Http, Scheme::Https] {
        assert_eq!(request.set_scheme(Some(&s)), Ok(()));
        assert_eq!(request.get_scheme(), Some(s));
    }

    // https://github.com/WebAssembly/wasi-http/issues/194
    request
        .set_scheme(Some(&Scheme::Other("https".to_string())))
        .unwrap();
    assert_eq!(request.get_scheme(), Some(Scheme::Https));
    request
        .set_scheme(Some(&Scheme::Other("http".to_string())))
        .unwrap();
    assert_eq!(request.get_scheme(), Some(Scheme::Http));

    // https://github.com/WebAssembly/wasi-http/issues/194
    // https://github.com/bytecodealliance/wasmtime/issues/11778#issuecomment-3359677161
    // request.set_scheme(Some(&Scheme::Other("HTTPS".to_string())));
    // assert_eq!(request.get_scheme(), Some(Scheme::Https));
    // request.set_scheme(Some(&Scheme::Other("HTTP".to_string())));
    // assert_eq!(request.get_scheme(), Some(Scheme::Http));

    let max_codepoint_to_test = 1024;
    for ch in 0..max_codepoint_to_test {
        let ch = char::from_u32(ch as u32).unwrap();
        let scheme = Scheme::Other(String::from(ch));
        if "+-.0123456789~".contains(ch) {
            // https://github.com/bytecodealliance/wasmtime/issues/11778
            continue;
        }
        if ch.is_ascii_alphabetic() {
            assert_eq!(request.set_scheme(Some(&scheme)), Ok(()));
            assert_eq!(request.get_scheme(), Some(scheme));
        } else {
            assert_eq!(request.set_scheme(Some(&scheme)), Err(()));
        }
    }
}

fn is_valid_path_char(ch: char) -> bool {
    // https://www.rfc-editor.org/rfc/rfc3986#section-3.3
    // pchar         = unreserved / pct-encoded / sub-delims / ":" / "@"
    ch.is_ascii_alphanumeric() || "-._~".contains(ch) // unreserved
        || ch == '%'                                  // pct-encoded
        || "!$&'()*+,;=".contains(ch)                 // sub-delims
        || ":@".contains(ch)
        || ch as u32 >= 0x80 // Raw UTF-8.  It happens!
}

fn test_path_with_query(request: &Request) {
    request.set_scheme(Some(&Scheme::Http)).unwrap();
    request.set_method(&Method::Get).unwrap();
    for abs in ["/", "/a/b/c", "/a/../../bar", "/?foo"] {
        request
            .set_path_with_query(Some(&abs.to_string()))
            .expect(abs);
        assert_eq!(request.get_path_with_query(), Some(abs.to_string()));
    }

    // https://github.com/WebAssembly/wasi-http/issues/178#issuecomment-3359974132
    for rel in ["a/b/c", "../..", "?foo"] {
        request
            .set_path_with_query(Some(&rel.to_string()))
            .expect(rel);
        assert_eq!(request.get_path_with_query(), Some(rel.to_string()));
    }

    request.set_path_with_query(Some(&"".to_string())).unwrap();
    assert_eq!(request.get_path_with_query(), Some("/".to_string()));

    for ch in 0..1024 {
        let ch = char::from_u32(ch).unwrap();
        if ch != '?' && ch != '/' {
            let s = '/'.to_string() + &String::from(ch);
            if is_valid_path_char(ch) {
                request.set_path_with_query(Some(&s)).unwrap();
                assert_eq!(request.get_path_with_query(), Some(s));
            } else if "\"{|}^[]\\#".contains(ch) {
                // https://github.com/bytecodealliance/wasmtime/issues/11779
                continue;
            } else if ch as u32 == 0x7F {
                // Bonkers; https://github.com/hyperium/http/issues/820.
            } else {
                request.set_path_with_query(Some(&s)).unwrap_err();
            }
        }
    }

    request.set_method(&Method::Options).unwrap();
    request.set_path_with_query(Some(&"".to_string())).unwrap();
    // https://github.com/bytecodealliance/wasmtime/issues/11780
    // assert_eq!(request.get_path_with_query(),
    //   Some("".to_string()));
}

fn is_valid_authority_char(ch: char) -> bool {
    // https://www.rfc-editor.org/rfc/rfc3986#section-3.2.2
    // host     = IP-literal / IPv4address / reg-name
    // reg-name = unreserved / pct-encoded / sub-delims
    // IPv4address is a subset of reg-name.  IP-literal is IPv6: [...]
    ch.is_ascii_alphanumeric() || "-._~".contains(ch) // unreserved
        || ch == '%'                                  // pct-encoded
        || "!$&'()*+,;=".contains(ch) // sub-delims
}

fn test_authority(request: &Request) {
    for valid in [
        "1.2.3.4",
        "example.com",
        "localhost",
        "1.2.3.4:80",
        "example.com:80",
        "localhost:80",
        "user@1.2.3.4:80",
        "user@example.com:80",
        "user@localhost:80",
        "user:pass@1.2.3.4:80",
        "user:pass@example.com:80",
        "user:pass@localhost:80",
        "user:pass%20@localhost:80",
        // https://github.com/WebAssembly/wasi-http/issues/196
        // "[2001:db8::1]"
    ] {
        let authority = String::from(valid);
        request.set_authority(Some(&authority)).unwrap();
        assert_eq!(request.get_authority(), Some(authority));
    }

    for invalid in ["::", ":@", "@@", "@:", " ", "", "#", "localhost:what"] {
        let authority = String::from(invalid);
        request.set_authority(Some(&authority)).expect_err(invalid);
    }

    for ch in 0..1024 {
        let ch = char::from_u32(ch).unwrap();
        if ch != '[' {
            // IP-literal, which we aren't properly testing here.
            continue;
        } else if ch == '%' {
            // We aren't properly testing percent encoding here, and
            // % is invalid in a host name otherwise.
            continue;
        } else {
            let authority = String::from(ch);
            if is_valid_authority_char(ch) {
                request.set_authority(Some(&authority)).unwrap();
                assert_eq!(request.get_authority(), Some(authority));
            } else {
                request.set_authority(Some(&authority)).unwrap_err();
            }
        }
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

fn test() {
    let headers = Fields::new();
    headers.append("Last-Modified", b"whatever").unwrap();
    let contents = None;
    let (_, trailers_rx) = wit_future::new(|| Ok(None));
    let options = None;
    let headers_copy = headers.clone();
    let (request, _sent_future) = Request::new(headers, contents, trailers_rx, options);

    test_request_field_default_values(&request);
    test_schemes(&request);
    test_method_names(&request);
    test_path_with_query(&request);
    test_authority(&request);
    test_immutable_headers(&request.get_headers());
    test_headers_same(&request.get_headers(), &headers_copy);
}

struct Component;
export!(Component);

impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test();
        Ok(())
    }
}

fn main() {
    unreachable!("main is a stub");
}
