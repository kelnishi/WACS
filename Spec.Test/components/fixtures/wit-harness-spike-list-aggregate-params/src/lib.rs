wit_bindgen::generate!({
    path: "wit",
    world: "router",
});

struct Example;

impl Guest for Example {
    fn tuple_list_sum(pairs: Vec<(u32, u32)>) -> u32 {
        pairs.iter().map(|(a, b)| a.wrapping_add(*b))
            .fold(0u32, u32::wrapping_add)
    }

    fn tuple_list_format(items: Vec<(u32, String)>) -> String {
        items.iter().map(|(n, s)| format!("{}:{}", n, s))
            .collect::<Vec<_>>().join(",")
    }

    fn option_list_sum(values: Vec<Option<u32>>) -> u32 {
        values.iter().filter_map(|v| *v)
            .fold(0u32, u32::wrapping_add)
    }
}

export!(Example);
