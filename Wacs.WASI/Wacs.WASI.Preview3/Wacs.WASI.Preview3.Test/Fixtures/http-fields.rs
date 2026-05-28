use test_wasm32_wasip3::cli::{export, exports::wasi::cli::run::Guest};
use test_wasm32_wasip3::http::wasi::http::types::{Fields, HeaderError};

fn test_empty_fields_inner(fields: Fields) {
    assert!(!fields.has("foo"));
    assert!(fields.get("foo").is_empty());
    assert!(fields.get_and_delete("foo").unwrap().is_empty());
    fields.delete("foo").unwrap();
    fields.delete("other").unwrap();
    assert!(fields.copy_all().is_empty());
}

fn test_empty_fields() {
    let fields = Fields::new();
    let clone = fields.clone();
    test_empty_fields_inner(fields);
    test_empty_fields_inner(clone);
    test_empty_fields_inner(Fields::from_list(&[]).unwrap());
}

fn test_fields_with_foo_inner(fields: Fields) {
    assert!(fields.has("foo"));
    assert_eq!(fields.get("foo"), [b"bar".to_vec()]);
    fields.delete("foo").unwrap();
    assert!(!fields.has("foo"));
    assert!(fields.get("foo").is_empty());

    fields.set("foo", &[]).unwrap();
    assert!(!fields.has("foo"));
    assert!(fields.get("foo").is_empty());
    fields
        .set("foo", &[b"bar".to_vec(), b"baz".to_vec()])
        .unwrap();
    assert!(fields.has("foo"));
    assert_eq!(fields.get("foo"), [b"bar".to_vec(), b"baz".to_vec()]);
    assert_eq!(
        fields.get_and_delete("foo").unwrap(),
        [b"bar".to_vec(), b"baz".to_vec()]
    );
    assert!(fields.get_and_delete("foo").unwrap().is_empty());

    fields
        .set("foo", &[b"bar".to_vec(), b"baz".to_vec()])
        .unwrap();
    assert!(fields.has("foo"));
    assert_eq!(fields.get("foo"), [b"bar".to_vec(), b"baz".to_vec()]);
    assert_eq!(
        fields.get_and_delete("foo").unwrap(),
        [b"bar".to_vec(), b"baz".to_vec()]
    );
    assert!(fields.get_and_delete("foo").unwrap().is_empty());
    assert!(!fields.has("foo"));

    fields.append("foo", b"bar").unwrap();
    fields.append("foo", b"baz").unwrap();
    assert!(fields.has("foo"));
    assert_eq!(fields.get("foo"), [b"bar".to_vec(), b"baz".to_vec()]);
    assert_eq!(
        fields.get_and_delete("foo").unwrap(),
        [b"bar".to_vec(), b"baz".to_vec()]
    );
    assert!(fields.get_and_delete("foo").unwrap().is_empty());
    assert!(!fields.has("foo"));
}

fn test_fields_with_foo() {
    let fields = Fields::from_list(&[("foo".to_string(), b"bar".to_vec())]).unwrap();
    let clone = fields.clone();
    test_fields_with_foo_inner(fields);
    test_fields_with_foo_inner(clone);
}

fn test_invalid_field_name(field: &str) {
    let fields = Fields::new();
    assert!(!fields.has(field));
    assert!(fields.get(field).is_empty());
    assert_eq!(fields.delete(field), Err(HeaderError::InvalidSyntax));
    assert_eq!(
        fields.get_and_delete(field),
        Err(HeaderError::InvalidSyntax)
    );
    assert_eq!(
        fields.set(field, &[b"val".to_vec()]),
        Err(HeaderError::InvalidSyntax)
    );
    assert_eq!(
        fields.append(field, b"val"),
        Err(HeaderError::InvalidSyntax)
    );
    assert!(fields.copy_all().is_empty());
    assert!(!fields.has(field));
    assert!(fields.get(field).is_empty());

    assert_eq!(
        Fields::from_list(&[(field.to_string(), b"val".to_vec())]).unwrap_err(),
        HeaderError::InvalidSyntax
    );
}

fn test_valid_field_name(field: &str) {
    let fields = Fields::new();
    assert!(!fields.has(field));
    assert!(fields.get(field).is_empty());
    fields.delete(field).unwrap();
    assert!(fields.get_and_delete(field).unwrap().is_empty());
    fields.set(field, &[b"val".to_vec()]).unwrap();
    fields.append(field, b"val2").unwrap();
    assert_eq!(
        fields.copy_all(),
        [
            (field.to_string(), b"val".to_vec()),
            (field.to_string(), b"val2".to_vec())
        ]
    );
    assert_eq!(
        Fields::from_list(&[
            (field.to_string(), b"val".to_vec()),
            (field.to_string(), b"val2".to_vec())
        ])
        .unwrap()
        .copy_all(),
        fields.clone().copy_all()
    );
}

fn test_invalid_field_value(val: &[u8]) {
    let fields = Fields::new();
    assert_eq!(
        fields.set("foo", &[val.to_vec()]),
        Err(HeaderError::InvalidSyntax)
    );
    assert_eq!(fields.append("foo", val), Err(HeaderError::InvalidSyntax));
    assert_eq!(
        Fields::from_list(&[("foo".to_string(), val.to_vec())]).unwrap_err(),
        HeaderError::InvalidSyntax
    );
}

fn test_valid_field_value(val: &[u8]) {
    let fields = Fields::new();
    fields.set("foo", &[val.to_vec()]).unwrap();
    fields.append("foo", val).unwrap();
    assert_eq!(
        fields.copy_all(),
        [
            ("foo".to_string(), val.to_vec()),
            ("foo".to_string(), val.to_vec())
        ]
    );
    assert_eq!(
        Fields::from_list(&[
            ("foo".to_string(), val.to_vec()),
            ("foo".to_string(), val.to_vec())
        ])
        .unwrap()
        .copy_all(),
        fields.clone().copy_all()
    );
}

fn compute_valid_field_name_chars(len: usize) -> Vec<bool> {
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
    for ch in "!#$%&'*+-.^_`|~".chars() {
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

fn test_invalid_field_names() {
    test_invalid_field_name("");
    test_invalid_field_name("voilà");
    test_invalid_field_name("hey ho");
    test_invalid_field_name(" ");
    test_invalid_field_name(" hey");
    test_invalid_field_name("hey ");
    test_invalid_field_name("(what)");
    test_invalid_field_name("[what]");
    test_invalid_field_name("{what}");
    // https://github.com/bytecodealliance/wasmtime/issues/11771
    // test_invalid_field_name("\"foo\"");
    let max_codepoint_to_test = 1024;
    let valid = compute_valid_field_name_chars(max_codepoint_to_test);
    for ch in 0..max_codepoint_to_test {
        if !valid[ch] {
            let ch = char::from_u32(ch as u32).unwrap();
            if ch == '"' {
                // https://github.com/bytecodealliance/wasmtime/issues/11771
                continue;
            }
            test_invalid_field_name(&String::from(ch));
        }
    }
}

fn test_valid_field_names() {
    let max_codepoint_to_test = 1024;
    let valid = compute_valid_field_name_chars(max_codepoint_to_test);
    for ch in 0..max_codepoint_to_test {
        if valid[ch] {
            let ch = char::from_u32(ch as u32).unwrap();
            if ch.is_uppercase() {
                // https://github.com/bytecodealliance/wasmtime/issues/11770
                continue;
            }
            test_valid_field_name(&String::from(ch));
        }
    }

    test_valid_field_name("1");
    test_valid_field_name("142");
    // https://github.com/bytecodealliance/wasmtime/issues/11770
    // test_valid_field_name("Foo");
    // test_valid_field_name("ConnectionLevel142");
    test_valid_field_name("kebab-data-100");
    test_valid_field_name(str::from_utf8(&[b"f"[0]; 1024]).unwrap());
}

fn compute_valid_field_value_bytes() -> Vec<bool> {
    // https://www.rfc-editor.org/rfc/rfc9110.html#section-5.6.2
    //  field-value    = *field-content
    //  field-content  = field-vchar
    //                   [ 1*( SP / HTAB / field-vchar ) field-vchar ]
    //  field-vchar    = VCHAR / obs-text
    //  VCHAR          = %x21-7E
    //  obs-text       = %x80-FF
    let mut ret = Vec::<bool>::new();
    ret.resize(256, false);
    ret[' ' as usize] = true;
    ret['\t' as usize] = true;
    for ch in 0x21..=0x7e {
        ret[ch] = true;
    }
    for ch in 0x80..=0xff {
        ret[ch] = true;
    }
    ret
}

fn test_invalid_field_values() {
    let valid = compute_valid_field_value_bytes();
    for byte in 0u8..=0xff {
        if !valid[byte as usize] {
            test_invalid_field_value(&[byte]);
        }
    }

    test_invalid_field_value(b"\n");
    test_invalid_field_value(b"\r");
    test_invalid_field_value(b"\0");
}

fn test_valid_field_values() {
    let valid = compute_valid_field_value_bytes();
    for byte in 0u8..=0xff {
        if valid[byte as usize] {
            test_valid_field_value(&[byte])
        }
    }

    test_valid_field_value(b"");
    test_valid_field_value(b" \t \t \t \t \t ");
    test_valid_field_value(b"Foo");
    test_valid_field_value(b"ConnectionLevel142");
    test_valid_field_value(b"kebab-data-100");
    test_valid_field_value(&[b"f"[0]; 1024]);
}

fn test_field_name_case_insensitivity() {
    let lower = "foo";
    let upper = "FOO";

    let fields = Fields::new();
    fields.append(lower, b"val1").unwrap();
    assert!(fields.has(lower));
    assert!(fields.has(upper));
    assert_eq!(fields.get(lower), fields.get(upper));
    fields.delete(upper).unwrap();
    assert!(!fields.has(lower));
    assert!(!fields.has(upper));

    fields.append(upper, b"val1").unwrap();
    assert!(fields.has(upper));
    assert!(fields.has(lower));
    assert_eq!(fields.get(lower), fields.get(upper));
    fields.delete(lower).unwrap();
    assert!(!fields.has(upper));
    assert!(!fields.has(lower));

    fields.append(lower, b"val1").unwrap();
    fields.append(upper, b"val2").unwrap();
    assert_eq!(
        fields.copy_all(),
        [
            (lower.to_string(), b"val1".to_vec()),
            (lower.to_string(), b"val2".to_vec())
        ]
    );
    assert_eq!(
        fields.get_and_delete(upper).unwrap(),
        [b"val1".to_vec(), b"val2".to_vec()]
    );

    fields.append(upper, b"val2").unwrap();
    fields.append(lower, b"val1").unwrap();
    // https://github.com/bytecodealliance/wasmtime/issues/11770
    // assert_eq!(fields.copy_all(),
    //            [(upper.to_string(), b"val2".to_vec()),
    //             (upper.to_string(), b"val1".to_vec())]);
    assert_eq!(
        fields.get_and_delete(lower).unwrap(),
        [b"val2".to_vec(), b"val1".to_vec()]
    );
}

struct Component;
export!(Component);
impl Guest for Component {
    async fn run() -> Result<(), ()> {
        test_empty_fields();
        test_fields_with_foo();
        test_invalid_field_names();
        test_valid_field_names();
        test_invalid_field_values();
        test_valid_field_values();
        test_field_name_case_insensitivity();
        Ok(())
    }
}

fn main() {
    unreachable!("main is a stub");
}
