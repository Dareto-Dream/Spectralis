//! Manual visual-verification aid, not part of the shipped crate: renders one
//! frame of the test scene and writes it to a PNG so a human (or Claude, via
//! the Read tool) can actually look at it, since there is no GUI/screenshot
//! automation harness in this repo. Run with `cargo run --example dump_frame`.

use wgpu_host::*;

fn main() {
    let (width, height) = (480u32, 360u32);
    let handle = wgpu_host_create(width, height);
    if handle.is_null() {
        eprintln!("no compatible GPU adapter on this machine");
        std::process::exit(1);
    }

    let ok = wgpu_host_render(handle, 0.7, 0.5, 0.35, 3.2);
    if !ok {
        eprintln!("render failed");
        std::process::exit(1);
    }

    let mut ptr: *const u8 = std::ptr::null();
    let mut len: usize = 0;
    if !wgpu_host_pixels(handle, &mut ptr, &mut len) {
        eprintln!("pixel readback failed");
        std::process::exit(1);
    }

    let pixels = unsafe { std::slice::from_raw_parts(ptr, len) }.to_vec();
    wgpu_host_destroy(handle);

    let img = image::RgbaImage::from_raw(width, height, pixels).expect("pixel buffer size mismatch");
    let out_path = std::env::args().nth(1).unwrap_or_else(|| "frame.png".to_string());
    img.save(&out_path).expect("failed to write PNG");
    println!("wrote {out_path}");
}
