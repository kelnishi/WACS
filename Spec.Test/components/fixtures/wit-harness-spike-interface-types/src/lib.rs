wit_bindgen::generate!({
    path: "wit",
    world: "cartographer",
});

use exports::wacs::interface_types_spike::geometry::{
    Guest as GeometryGuest, Point, Quadrant, Region,
};

struct Example;

impl GeometryGuest for Example {
    fn classify(p: Point) -> Quadrant {
        match (p.x, p.y) {
            (x, y) if x >= 100 && y >= 100 => Quadrant::Ne,
            (x, y) if x < 100 && y >= 100 => Quadrant::Nw,
            (x, y) if x < 100 && y < 100 => Quadrant::Sw,
            _ => Quadrant::Se,
        }
    }

    fn describe(r: Region) -> String {
        match r {
            Region::Empty => "(empty)".to_string(),
            Region::PointOnly(p) => format!("point({},{})", p.x, p.y),
            Region::Labeled(s) => format!("label<{}>", s),
        }
    }
}

export!(Example);
