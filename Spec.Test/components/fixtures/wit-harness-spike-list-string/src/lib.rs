wit_bindgen::generate!({
    path: "wit",
    world: "catalog",
});

struct Example;

impl Guest for Example {
    fn get_titles() -> Vec<String> {
        vec![
            "first".to_string(),
            "second".to_string(),
            "third".to_string(),
        ]
    }
}

export!(Example);
