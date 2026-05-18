wit_bindgen::generate!({
    path: "wit",
    world: "richer",
});

struct Example;

impl Guest for Example {
    fn add(a: Vec2, b: Vec2) -> Vec2 {
        Vec2 {
            x: a.x + b.x,
            y: a.y + b.y,
        }
    }

    fn normalize_or_fail(v: Vec2) -> Outcome {
        if v.x == 0 && v.y == 0 {
            Outcome::Invalid
        } else {
            Outcome::Success(Vec2 {
                x: v.x.signum(),
                y: v.y.signum(),
            })
        }
    }
}

export!(Example);
