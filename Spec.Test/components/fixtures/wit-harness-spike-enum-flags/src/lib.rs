wit_bindgen::generate!({
    path: "wit",
    world: "security",
});

struct Example;

impl Guest for Example {
    fn get_status() -> Status {
        Status {
            sev: Severity::Warning,
            perms: Permissions::READ | Permissions::WRITE,
        }
    }
}

export!(Example);
