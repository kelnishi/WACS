wit_bindgen::generate!({
    path: "wit",
    world: "calculator",
});

use exports::wacs::interface_export_spike::ops::Guest as OpsGuest;

struct Example;

impl Guest for Example {
    fn bake() -> u32 {
        77
    }
}

impl OpsGuest for Example {
    fn add(a: u32, b: u32) -> u32 {
        a.wrapping_add(b)
    }

    fn swap(a: u32, b: u32) -> (u32, u32) {
        (b, a)
    }
}

export!(Example);
