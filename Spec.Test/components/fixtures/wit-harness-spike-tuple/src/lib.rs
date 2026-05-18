wit_bindgen::generate!({
    path: "wit",
    world: "tagger",
});

struct Example;

impl Guest for Example {
    fn tag(x: u32, y: u32) -> Entry {
        Entry {
            coord: (x, y),
            labeled: ("point".to_string(), x.wrapping_add(y)),
        }
    }
}

export!(Example);
