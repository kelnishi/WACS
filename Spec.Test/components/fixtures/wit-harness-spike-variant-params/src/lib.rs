wit_bindgen::generate!({
    path: "wit",
    world: "router",
});

struct Example;

impl Guest for Example {
    fn describe_signal(s: Signal) -> String {
        match s {
            Signal::Silence => "silence".to_string(),
            Signal::Ping => "ping!".to_string(),
            Signal::Message(text) => format!("msg<{}>", text),
        }
    }
}

export!(Example);
