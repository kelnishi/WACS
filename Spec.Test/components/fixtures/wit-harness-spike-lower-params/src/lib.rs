wit_bindgen::generate!({
    path: "wit",
    world: "repeater",
});

struct Example;

impl Guest for Example {
    fn shout(name: String, count: u32) -> String {
        let mut out = String::new();
        for _ in 0..count {
            out.push_str(&name);
            out.push('!');
        }
        out
    }

    fn length_of(text: String) -> u32 {
        text.len() as u32
    }

    fn sum(values: Vec<u32>) -> u32 {
        values.into_iter().fold(0u32, |acc, x| acc.wrapping_add(x))
    }

    fn total_chars(words: Vec<String>) -> u32 {
        words.iter().map(|w| w.len() as u32).sum()
    }
}

export!(Example);
