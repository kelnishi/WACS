wit_bindgen::generate!({
    path: "wit",
    world: "numbers",
});

struct Example;

impl Guest for Example {
    fn get_numbers() -> Vec<u32> {
        vec![100, 200, 300, 400]
    }
}

export!(Example);
