wit_bindgen::generate!({
    path: "wit",
    world: "adder",
});

struct Example;

impl Guest for Example {
    fn upgrade(input: Result<u32, u64>) -> u64 {
        match input {
            Ok(v) => (v as u64).wrapping_add(1),
            Err(e) => e.wrapping_mul(2),
        }
    }
}

export!(Example);
