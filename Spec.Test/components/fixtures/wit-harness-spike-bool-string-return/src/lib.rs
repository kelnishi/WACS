wit_bindgen::generate!({
    path: "wit",
    world: "ops",
});

struct Example;

impl Guest for Example {
    fn greet(use_comma: bool) -> String {
        if use_comma {
            "Hello, World!".to_string()
        } else {
            "Hello World!".to_string()
        }
    }
}

export!(Example);
