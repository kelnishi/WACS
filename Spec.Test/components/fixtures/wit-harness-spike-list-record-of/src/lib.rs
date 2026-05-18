wit_bindgen::generate!({
    path: "wit",
    world: "geo",
});

struct Example;

impl Guest for Example {
    fn get_points() -> Vec<Point> {
        vec![
            Point { x: 1, y: 2 },
            Point { x: -3, y: 4 },
            Point { x: 5, y: -6 },
        ]
    }
}

export!(Example);
