wit_bindgen::generate!({
    path: "wit",
    world: "dispatcher",
});

struct Example;

impl Guest for Example {
    fn prefer_ok(input: Result<u32, u32>) -> u32 {
        match input {
            Ok(v) => v.wrapping_mul(2),
            Err(e) => e.wrapping_add(1000),
        }
    }

    fn render(input: Result<String, String>) -> String {
        match input {
            Ok(s) => format!("ok({})", s),
            Err(s) => format!("err({})", s),
        }
    }

    fn note(input: Result<(), ()>) -> u32 {
        match input {
            Ok(()) => 1,
            Err(()) => 2,
        }
    }
}

export!(Example);
