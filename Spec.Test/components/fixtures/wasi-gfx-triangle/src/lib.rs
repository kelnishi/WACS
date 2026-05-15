// CPU-path parity fixture for WACS.WASI.GFX v0. Opens a
// surface, fills a centered isoceles triangle every frame.
// Toggles fill color (cyan / magenta) on pointer-up. Same
// scaffolding as the rectangle fixture — just different
// rasterizer.

wit_bindgen::generate!({
    path: "wit",
    world: "triangle",
    generate_all,
});

export!(Tri);

use wasi::{frame_buffer::frame_buffer, graphics_context::graphics_context, surface::surface};

struct Tri;

impl Guest for Tri {
    fn start() {
        draw();
    }
}

fn draw() {
    let canvas = surface::Surface::new(surface::CreateDesc {
        height: None,
        width: None,
    });
    let ctx = graphics_context::Context::new();
    canvas.connect_graphics_context(&ctx);

    let device = frame_buffer::Device::new();
    device.connect_graphics_context(&ctx);

    let pointer_up = canvas.subscribe_pointer_up();
    let resize = canvas.subscribe_resize();
    let frame = canvas.subscribe_frame();
    let pollables = vec![&pointer_up, &resize, &frame];

    let mut cyan = true;
    let mut width = canvas.width();
    let mut height = canvas.height();

    loop {
        let ready = wasi::io::poll::poll(&pollables);

        if ready.contains(&0) {
            let _ = canvas.get_pointer_up();
            cyan = !cyan;
        }

        if ready.contains(&1) {
            if let Some(ev) = canvas.get_resize() {
                width = ev.width;
                height = ev.height;
            }
        }

        if ready.contains(&2) {
            canvas.get_frame();

            let ab = ctx.get_current_buffer();
            let buffer = frame_buffer::Buffer::from_graphics_buffer(ab);

            let fill: u32 = if cyan {
                u32::from_le_bytes([0x00, 0xC0, 0xC0, 0xFF])
            } else {
                u32::from_le_bytes([0xC0, 0x00, 0xC0, 0xFF])
            };
            let bg: u32 = u32::from_le_bytes([0x10, 0x10, 0x10, 0xFF]);

            // Isoceles triangle inscribed in the central
            // 60% of the canvas. Apex up.
            let cx = (width / 2) as i32;
            let half = (width as i32 * 3 / 10).max(8);
            let top_y = (height as i32 / 5).max(4);
            let bot_y = (height as i32 * 4 / 5).max(top_y + 16);
            let v0 = (cx, top_y);
            let v1 = (cx - half, bot_y);
            let v2 = (cx + half, bot_y);

            let mut buf = vec![bg; (width * height) as usize];
            for y in 0..height as i32 {
                for x in 0..width as i32 {
                    if point_in_tri(v0, v1, v2, (x, y)) {
                        let idx = (y as u32 * width + x as u32) as usize;
                        if idx < buf.len() {
                            buf[idx] = fill;
                        }
                    }
                }
            }

            buffer.set(bytemuck::cast_slice(&buf));
            ctx.present();
        }
    }
}

fn edge((ax, ay): (i32, i32), (bx, by): (i32, i32), (px, py): (i32, i32)) -> i32 {
    (px - ax) * (by - ay) - (py - ay) * (bx - ax)
}

fn point_in_tri(v0: (i32, i32), v1: (i32, i32), v2: (i32, i32), p: (i32, i32)) -> bool {
    let e0 = edge(v0, v1, p);
    let e1 = edge(v1, v2, p);
    let e2 = edge(v2, v0, p);
    (e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0)
}
