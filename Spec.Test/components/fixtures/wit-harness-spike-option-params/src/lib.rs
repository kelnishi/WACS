wit_bindgen::generate!({
    path: "wit",
    world: "greeter",
});

struct Example;

impl Guest for Example {
    fn double_or(value: Option<u32>, fallback: u32) -> u32 {
        value.map(|v| v.wrapping_mul(2)).unwrap_or(fallback)
    }

    fn greet(name: Option<String>) -> String {
        match name {
            Some(n) => format!("hello, {}!", n),
            None => "hello, stranger!".to_string(),
        }
    }
}

export!(Example);
