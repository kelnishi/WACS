wit_bindgen::generate!({
    path: "wit",
    world: "streams",
});

struct Example;

impl Guest for Example {
    fn get_payload(want_numbers: u32) -> Payload {
        if want_numbers != 0 {
            Payload::Numbers(vec![7, 14, 21, 28])
        } else {
            Payload::Empty
        }
    }
}

export!(Example);
