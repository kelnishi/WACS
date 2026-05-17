wit_bindgen::generate!({
    path: "wit",
    world: "greeter",
});

struct Example;

impl Guest for Example {
    fn greet() -> Greeting {
        Greeting {
            message: "Hello, World!".to_string(),
            count: 42,
        }
    }
}

export!(Example);
