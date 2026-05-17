wit_bindgen::generate!({
    path: "wit",
    world: "messages",
});

struct Example;

impl Guest for Example {
    fn pick(want_hello: u32) -> Message {
        if want_hello != 0 {
            Message::Hello("Hello, World!".to_string())
        } else {
            Message::Silence
        }
    }
}

export!(Example);
