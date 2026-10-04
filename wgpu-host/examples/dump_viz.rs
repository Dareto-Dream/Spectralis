//! Manual visual-verification aid, not part of the shipped crate: renders each built-in visualizer with a
//! synthetic spectrum and writes `viz-<name>.png` into the directory given as the first argument (default:
//! the current one), so a human (or Claude, via the Read tool) can look at them. Run with
//! `cargo run --example dump_viz -- <out dir>`.

use wgpu_host::viz::*;

fn main() {
    let out_dir = std::env::args().nth(1).unwrap_or_else(|| ".".to_string());
    let (width, height) = (640u32, 360u32);

    let handle = wgpu_viz_create(width, height);
    if handle.is_null() {
        eprintln!("no compatible GPU adapter on this machine");
        std::process::exit(1);
    }

    // A bass-heavy rolling spectrum, roughly what music looks like.
    let levels: Vec<f32> = (0..128)
        .map(|i| {
            let x = i as f32 / 127.0;
            let body = (1.0 - x).powf(1.6);
            (body * (0.75 + 0.25 * (i as f32 * 0.9).sin())).clamp(0.0, 1.0)
        })
        .collect();

    wgpu_viz_set_audio(handle, levels.as_ptr(), levels.len() as u32, 0.55, 0.9);
    wgpu_viz_set_accent(handle, 1.0, 0.31, 0.10);

    for index in 0..wgpu_viz_builtin_count() {
        assert!(wgpu_viz_use_builtin(handle, index), "built-in {index} failed to compile");
        assert!(wgpu_viz_render(handle, 3.2), "render failed");

        let mut ptr: *const u8 = std::ptr::null();
        let mut len = 0usize;
        assert!(wgpu_viz_pixels(handle, &mut ptr, &mut len));
        let pixels = unsafe { std::slice::from_raw_parts(ptr, len) }.to_vec();

        let img = image::RgbaImage::from_raw(width, height, pixels).expect("pixel buffer size mismatch");
        let name = BUILTINS[index as usize].0;
        let path = format!("{out_dir}/viz-{name}.png");
        img.save(&path).expect("failed to write PNG");
        println!("wrote {path}");
    }

    wgpu_viz_destroy(handle);
}
