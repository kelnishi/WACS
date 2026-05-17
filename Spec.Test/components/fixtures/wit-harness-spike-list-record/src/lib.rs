wit_bindgen::generate!({
    path: "wit",
    world: "numbers",
});

struct Example;

impl Guest for Example {
    fn get_bag() -> Bag {
        Bag {
            values: vec![10, 20, 30, 40, 50],
            count: 5,
        }
    }
}

export!(Example);
