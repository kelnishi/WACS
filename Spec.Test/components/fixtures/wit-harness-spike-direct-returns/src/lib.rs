wit_bindgen::generate!({
    path: "wit",
    world: "directs",
});

struct Example;

impl Guest for Example {
    fn find_positive(value: i32) -> Option<u32> {
        if value > 0 { Some(value as u32) } else { None }
    }

    fn ensure_non_empty(value: String) -> Option<String> {
        if value.is_empty() { None } else { Some(value) }
    }

    fn parse_int(text: String) -> Result<u32, String> {
        text.parse::<u32>().map_err(|e| format!("parse error: {}", e))
    }

    fn coord_named(x: u32, y: u32, label: String) -> (u32, u32, String) {
        (x, y, label)
    }
}

export!(Example);
