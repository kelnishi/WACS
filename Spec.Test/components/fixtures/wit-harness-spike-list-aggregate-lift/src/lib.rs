wit_bindgen::generate!({
    path: "wit",
    world: "collector",
});

struct Example;

impl Guest for Example {
    fn sparse_values() -> Vec<Option<u32>> {
        vec![Some(10), None, Some(30), None, Some(50)]
    }

    fn pairs() -> Vec<(u32, String)> {
        vec![(1, "one".to_string()), (2, "two".to_string()), (3, "three".to_string())]
    }

    fn signals() -> Vec<Signal> {
        vec![Signal::Off, Signal::High, Signal::Low, Signal::High]
    }
}

export!(Example);
