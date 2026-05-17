wit_bindgen::generate!({
    path: "wit",
    world: "stats",
});

struct Example;

impl Guest for Example {
    fn get_sample() -> Sample {
        Sample {
            flag: true,
            small_s: -7,
            small_u: 200,
            med_s: -1000,
            med_u: 50000,
            big_s: -9_000_000_000,
            big_u: 18_000_000_000_000_000_000,
            single: 3.14,
            double: 2.718281828,
            letter: 'Z',
        }
    }
}

export!(Example);
