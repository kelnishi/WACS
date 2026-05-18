wit_bindgen::generate!({
    path: "wit",
    world: "inventory",
});

struct Example;

impl Guest for Example {
    fn inventory_value(items: Vec<Item>, unit_price: u32) -> u32 {
        items.iter().map(|i| i.qty.wrapping_mul(unit_price)).fold(0u32, u32::wrapping_add)
    }

    fn inventory_summary(items: Vec<Item>) -> String {
        items.iter().map(|i| format!("{}:{}", i.sku, i.qty))
            .collect::<Vec<_>>().join(",")
    }
}

export!(Example);
