wit_bindgen::generate!({
    path: "wit",
    world: "picker",
});

struct Example;

impl Guest for Example {
    fn pick(want_num: u32, want_name: u32) -> Snapshot {
        Snapshot {
            maybe_num: if want_num != 0 { Some(42) } else { None },
            maybe_name: if want_name != 0 { Some("hi".to_string()) } else { None },
        }
    }
}

export!(Example);
