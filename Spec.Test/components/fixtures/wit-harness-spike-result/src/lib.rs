wit_bindgen::generate!({
    path: "wit",
    world: "parser",
});

struct Example;

impl Guest for Example {
    fn parse() -> Outcome {
        Outcome {
            from_positive: Ok(42),
            from_negative: Err("not a positive integer".to_string()),
            empty_check: Ok(()),
        }
    }
}

export!(Example);
