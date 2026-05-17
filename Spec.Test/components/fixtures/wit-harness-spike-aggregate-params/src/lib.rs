wit_bindgen::generate!({
    path: "wit",
    world: "classifier",
});

struct Example;

impl Guest for Example {
    fn rank(p: Priority, ch: Channels) -> u32 {
        let priority_weight = match p {
            Priority::Low => 1u32,
            Priority::Normal => 10,
            Priority::High => 100,
        };
        // count bits in flags
        let bits = ch.bits() as u32;
        let mut count = 0u32;
        let mut b = bits;
        while b != 0 {
            count += b & 1;
            b >>= 1;
        }
        priority_weight * 1000 + count
    }

    fn render_point(p: (u32, u32, String)) -> String {
        format!("{}=({}, {})", p.2, p.0, p.1)
    }
}

export!(Example);
