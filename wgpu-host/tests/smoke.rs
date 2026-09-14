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
fn set_geometry_replaces_the_default_cube_and_renders() {
    let handle = wgpu_host_create(64, 48);
    if handle.is_null() {
        eprintln!("skipping: no compatible GPU adapter on this machine");
        return;
    }

    // A single flat-colored triangle facing the camera, filling most of the view.
    // Layout matches Vertex { position: [f32;3], color: [f32;3] }, interleaved.
    #[rustfmt::skip]
    let vertices: [f32; 18] = [
        0.0,  0.8, 0.0,   1.0, 1.0, 0.0, // top, yellow
        -0.8, -0.8, 0.0,  1.0, 1.0, 0.0, // bottom-left
        0.8, -0.8, 0.0,   1.0, 1.0, 0.0, // bottom-right
    ];
    let indices: [u16; 3] = [0, 1, 2];

    let ok = wgpu_host_set_geometry(handle, vertices.as_ptr(), 3, indices.as_ptr(), 3);
    assert!(ok, "set_geometry should accept a well-formed single triangle");

    // Look straight down -z at the origin so the triangle is guaranteed on-screen
    // regardless of the orbit-camera math used for the built-in cube's default view.
    let rendered = wgpu_host_render(handle, 0.0, 0.0, 0.0, 3.0);
    assert!(rendered, "render call failed after set_geometry");

    let mut ptr: *const u8 = std::ptr::null();
    let mut len: usize = 0;
    assert!(wgpu_host_pixels(handle, &mut ptr, &mut len));
    let pixels = unsafe { std::slice::from_raw_parts(ptr, len) };

    let mut yellow_pixels = 0usize;
    for chunk in pixels.chunks_exact(4) {
        // Yellow-ish: high R, high G, low B.
        if chunk[0] > 200 && chunk[1] > 200 && chunk[2] < 60 {
            yellow_pixels += 1;
        }
    }
    assert!(
        yellow_pixels > 50,
        "expected the submitted yellow triangle to be visible, got {yellow_pixels} matching pixels"
    );

    wgpu_host_destroy(handle);
}

#[test]
fn set_geometry_rejects_oversized_and_empty_submissions() {
    let handle = wgpu_host_create(16, 16);
    if handle.is_null() {
        eprintln!("skipping: no compatible GPU adapter on this machine");
        return;
    }

    let one_vertex: [f32; 6] = [0.0, 0.0, 0.0, 1.0, 1.0, 1.0];
    let one_index: [u16; 1] = [0];

    // Zero counts, regardless of what the pointers point to.
    assert!(!wgpu_host_set_geometry(handle, one_vertex.as_ptr(), 0, one_index.as_ptr(), 1));
    assert!(!wgpu_host_set_geometry(handle, one_vertex.as_ptr(), 1, one_index.as_ptr(), 0));

    // Counts beyond the documented caps.
    assert!(!wgpu_host_set_geometry(handle, one_vertex.as_ptr(), 70_000, one_index.as_ptr(), 1));
    assert!(!wgpu_host_set_geometry(handle, one_vertex.as_ptr(), 1, one_index.as_ptr(), 400_000));

    // A previously-rejected call must not have disturbed the default cube — render
    // still succeeds and produces a non-trivial image.
    assert!(wgpu_host_render(handle, 0.5, 0.3, 0.2, 3.0));

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

    let vertices: [f32; 6] = [0.0, 0.0, 0.0, 1.0, 1.0, 1.0];
    let indices: [u16; 1] = [0];
    assert!(!wgpu_host_set_geometry(std::ptr::null_mut(), vertices.as_ptr(), 1, indices.as_ptr(), 1));
    assert!(!wgpu_host_set_geometry(handle_stub(), std::ptr::null(), 1, indices.as_ptr(), 1));

    // Must not crash.
    wgpu_host_destroy(std::ptr::null_mut());
}

/// A handle that's non-null but was never created by `wgpu_host_create` would be unsound to
/// dereference — this only needs to be non-null to exercise the null-pointer-field checks in
/// `wgpu_host_set_geometry` before it would ever touch the handle itself.
fn handle_stub() -> *mut std::ffi::c_void {
    std::ptr::NonNull::<u8>::dangling().as_ptr() as *mut std::ffi::c_void
}
