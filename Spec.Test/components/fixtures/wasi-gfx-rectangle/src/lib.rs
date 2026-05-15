// Parity fixture for WACS.WASI.GFX v0. Draws a moving
// red/green rectangle on a CPU frame-buffer surface;
// toggles color on pointer-up and rerenders on each
// frame event. Mirrors the upstream
// `wasi-gfx-runtime/examples/apps/rectangle_frame_buffer`
// example with the wasi:webgpu world inclusion dropped
// (v0 covers graphics-context + surface + frame-buffer
// only).

wit_bindgen::generate!({
    path: "wit",
    world: "rectangle",
    generate_all,
});

export!(Example);

use std::cmp::min;
use wasi::{frame_buffer::frame_buffer, graphics_context::graphics_context, surface::surface};

struct Example;

impl Guest for Example {
    fn start() {
        draw_rectangle();
    }
}

fn draw_rectangle() {
    let canvas = surface::Surface::new(surface::CreateDesc {
        height: None,
        width: None,
    });
    let graphics_context = graphics_context::Context::new();
    canvas.connect_graphics_context(&graphics_context);

    let fb_device = frame_buffer::Device::new();
    fb_device.connect_graphics_context(&graphics_context);

    let pointer_up_pollable = canvas.subscribe_pointer_up();
    let resize_pollable = canvas.subscribe_resize();
    let frame_pollable = canvas.subscribe_frame();
    let pollables = vec![&pointer_up_pollable, &resize_pollable, &frame_pollable];

    let mut green = false;
    let mut height = canvas.height();
    let mut width = canvas.width();

    loop {
        let ready = wasi::io::poll::poll(&pollables);

        if ready.contains(&0) {
            let _ = canvas.get_pointer_up();
            green = !green;
        }

        if ready.contains(&1) {
            if let Some(ev) = canvas.get_resize() {
                height = ev.height;
                width = ev.width;
            }
        }

        if ready.contains(&2) {
            canvas.get_frame();

            let graphics_buffer = graphics_context.get_current_buffer();
            let buffer = frame_buffer::Buffer::from_graphics_buffer(graphics_buffer);

            // RGBA8 packed bytes — byte order R,G,B,A.
            const RED:   u32 = 0x00_00_00_FF_u32.to_be();      // 00 00 00 FF  → not quite
            // The above is a placeholder; we'll build the
            // pixel words below in native byte order then
            // cast to bytes.
            let _ = RED;

            let red:   u32 = u32::from_le_bytes([0xFF, 0x00, 0x00, 0xFF]);
            let green_c: u32 = u32::from_le_bytes([0x00, 0xFF, 0x00, 0xFF]);
            let gray:  u32 = u32::from_le_bytes([0x80, 0x80, 0x80, 0xFF]);

            let local_width = min(width, 100);
            let local_height = min(height, 100);
            let mut buf = vec![0u32; (width * height) as usize];
            for y in 0..local_height {
                for x in 0..local_width {
                    let color = if green { green_c } else { red };
                    let v = if is_on_rect(local_width, local_height, x, y) {
                        color
                    } else {
                        gray
                    };
                    let idx = (y * width + x) as usize;
                    if idx < buf.len() {
                        buf[idx] = v;
                    }
                }
            }

            buffer.set(bytemuck::cast_slice(&buf));
            graphics_context.present();
        }
    }
}

fn is_on_rect(width: u32, height: u32, x: u32, y: u32) -> bool {
    y == 1 || y == height.saturating_sub(2) || x == 1 || x == width.saturating_sub(2)
}
