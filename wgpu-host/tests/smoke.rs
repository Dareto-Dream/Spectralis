//! Exercises the real C ABI end to end against whatever GPU/backend this machine
//! actually has (DX12/Vulkan) — proves the offscreen render + readback pipeline
//! works, not just that it compiles. `wgpu_host_create` can legitimately return
//! null on a machine with no compatible adapter; tests skip (not fail) in that case
//! so CI without a GPU doesn't red the build for an environment limitation.

use wgpu_host::*;

#[test]
fn create_render_and_read_pixels_roundtrip() {
    let handle = wgpu_host_create(64, 48);
    if handle.is_null() {
        eprintln!("skipping: no compatible GPU adapter on this machine");
        return;
    }

    assert_eq!(wgpu_host_width(handle), 64);
    assert_eq!(wgpu_host_height(handle), 48);

    let ok = wgpu_host_render(handle, 0.6, 0.4, 0.3, 3.0);
    assert!(ok, "render call failed");

    let mut ptr: *const u8 = std::ptr::null();
    let mut len: usize = 0;
    let got = wgpu_host_pixels(handle, &mut ptr, &mut len);
    assert!(got, "pixel readback failed");
    assert_eq!(len, 64 * 48 * 4);

    let pixels = unsafe { std::slice::from_raw_parts(ptr, len) };

    // The clear color is a fixed dark blue-gray; if the cube actually rendered,
    // at least some pixels must differ from it (and from each other, since the
    // cube has distinct per-vertex colors on visible faces).
    let clear = [
        (0.05f32 * 255.0) as u8,
        (0.05f32 * 255.0) as u8,
        (0.08f32 * 255.0) as u8,
    ];
    let mut non_clear_pixels = 0usize;
    let mut distinct_colors = std::collections::HashSet::new();
    for chunk in pixels.chunks_exact(4) {
        let rgb = [chunk[0], chunk[1], chunk[2]];
        if rgb != clear {
            non_clear_pixels += 1;
        }
        distinct_colors.insert(rgb);
        assert_eq!(chunk[3], 255, "alpha channel should be fully opaque");
    }

    assert!(
        non_clear_pixels > 100,
        "expected a visible cube (many non-background pixels), got {non_clear_pixels}"
    );
    assert!(
        distinct_colors.len() > 3,
        "expected multiple distinct colors from the cube's per-vertex colors plus background, got {}",
        distinct_colors.len()
    );

    wgpu_host_destroy(handle);
}

#[test]
fn create_with_null_dimensions_still_produces_a_usable_renderer() {
    // 0x0 gets clamped to 1x1 rather than failing outright.
    let handle = wgpu_host_create(0, 0);
    if handle.is_null() {
        eprintln!("skipping: no compatible GPU adapter on this machine");
        return;
    }
    assert_eq!(wgpu_host_width(handle), 1);
    assert_eq!(wgpu_host_height(handle), 1);
    wgpu_host_destroy(handle);
}

#[test]
fn null_handle_calls_are_safe_no_ops() {
    assert!(!wgpu_host_render(std::ptr::null_mut(), 0.0, 0.0, 0.0, 0.0));
    assert_eq!(wgpu_host_width(std::ptr::null_mut()), 0);
    assert_eq!(wgpu_host_height(std::ptr::null_mut()), 0);

    let mut ptr: *const u8 = std::ptr::null();
    let mut len: usize = 0;
    assert!(!wgpu_host_pixels(std::ptr::null_mut(), &mut ptr, &mut len));

    // Must not crash.
    wgpu_host_destroy(std::ptr::null_mut());
}
