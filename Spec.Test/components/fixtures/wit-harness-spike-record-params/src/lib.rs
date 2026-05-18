wit_bindgen::generate!({
    path: "wit",
    world: "packager",
});

struct Example;

impl Guest for Example {
    fn describe(p: Parcel) -> String {
        format!("{} ({}x{}) tags={}",
            p.name, p.size.width, p.size.height, p.tags.join(","))
    }
}

export!(Example);
