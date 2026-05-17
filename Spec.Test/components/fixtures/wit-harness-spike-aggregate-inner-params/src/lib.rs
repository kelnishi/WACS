wit_bindgen::generate!({
    path: "wit",
    world: "batcher",
});

struct Example;

impl Guest for Example {
    fn sum_or_default(maybe_values: Option<Vec<u32>>, fallback: u32) -> u32 {
        match maybe_values {
            Some(v) => v.into_iter().fold(0u32, u32::wrapping_add),
            None => fallback,
        }
    }

    fn format_line(maybe_line: Option<Line>) -> String {
        match maybe_line {
            Some(l) => format!("{}@{}", l.text, l.weight),
            None => "(none)".to_string(),
        }
    }

    fn weighted_sum(p: (u32, Vec<u32>)) -> u32 {
        let (weight, values) = p;
        values.into_iter().fold(0u32, u32::wrapping_add).wrapping_mul(weight)
    }
}

export!(Example);
