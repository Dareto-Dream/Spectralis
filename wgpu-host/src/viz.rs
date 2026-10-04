//! GPU visualizer pipeline: a full-screen fragment shader fed with the audio, exposed over a C ABI.
//!
//! A visualizer is just one WGSL function:
//!
//! ```wgsl
//! fn viz(uv: vec2<f32>) -> vec4<f32>   // uv: (0,0) top-left .. (1,1) bottom-right
//! ```
//!
//! The host wraps it with everything else: a vertex stage that covers the screen, a uniform block
//! (`u.resolution`, `u.time`, `u.rms`, `u.peak`, `u.bin_count`, `u.accent`) and the spectrum as a texture
//! read through `spectrum_at(x)` (x in 0..1 across all bins, linearly interpolated). Shaders are compiled
//! inside a validation error scope, so a broken one is reported and rejected without disturbing the one
//! already running. Frames are rendered offscreen and read back as tightly-packed RGBA8, the same
//! hand-off the world renderer uses, so the .NET side composites them the same way.

use std::ffi::c_void;
use std::mem;

use bytemuck::{Pod, Zeroable};

const COLOR_FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8Unorm;
const COPY_BYTES_PER_ROW_ALIGNMENT: u32 = 256;

/// Spectrum bins the texture holds. Callers may pass fewer; extras are dropped.
pub const MAX_BINS: u32 = 512;

/// Cap on a submitted visualizer, so untrusted content can't make us compile megabytes of WGSL.
pub const MAX_SHADER_BYTES: usize = 64 * 1024;

const MAX_DIMENSION: u32 = 8192;

const PRELUDE: &str = r#"
struct VizUniform {
    resolution: vec2<f32>,
    time: f32,
    rms: f32,
    peak: f32,
    bin_count: f32,
    pad: vec2<f32>,
    accent: vec4<f32>,
};

@group(0) @binding(0) var<uniform> u: VizUniform;
@group(0) @binding(1) var spectrum_tex: texture_2d<f32>;

// Spectrum level at x in 0..1 across all bins, linearly interpolated between neighbouring bins.
fn spectrum_at(x: f32) -> f32 {
    let n = max(u.bin_count, 1.0);
    let f = clamp(x, 0.0, 1.0) * (n - 1.0);
    let i0 = i32(floor(f));
    let i1 = min(i0 + 1, i32(n) - 1);
    let t = f - floor(f);
    let a = textureLoad(spectrum_tex, vec2<i32>(i0, 0), 0).r;
    let b = textureLoad(spectrum_tex, vec2<i32>(i1, 0), 0).r;
    return mix(a, b, t);
}

struct VsOut {
    @builtin(position) pos: vec4<f32>,
    @location(0) uv: vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) i: u32) -> VsOut {
    // One oversized triangle covers the whole target; no vertex buffer needed.
    var corners = array<vec2<f32>, 3>(vec2<f32>(-1.0, -1.0), vec2<f32>(3.0, -1.0), vec2<f32>(-1.0, 3.0));
    var out: VsOut;
    out.pos = vec4<f32>(corners[i], 0.0, 1.0);
    out.uv = vec2<f32>((corners[i].x + 1.0) * 0.5, 1.0 - ((corners[i].y + 1.0) * 0.5));
    return out;
}

@fragment
fn fs_main(in: VsOut) -> @location(0) vec4<f32> {
    let c = viz(in.uv);
    return vec4<f32>(clamp(c.rgb, vec3<f32>(0.0), vec3<f32>(1.0)), 1.0);
}
"#;

/// The visualizers that ship with the host. Index order is part of the C ABI (`wgpu_viz_use_builtin`).
pub const BUILTINS: &[(&str, &str)] = &[
    (
        "bars",
        r#"
fn viz(uv: vec2<f32>) -> vec4<f32> {
    let bars = 48.0;
    let x = uv.x * bars;
    let idx = floor(x);
    let level = spectrum_at((idx + 0.5) / bars);
    let h = clamp(level, 0.0, 1.0) * 0.85;
    let y = 1.0 - uv.y;
    let within = fract(x);
    let bar = step(0.12, within) * step(within, 0.88);
    let fill = step(y, h) * bar;
    let glow = exp(-abs(y - h) * 18.0) * bar * 0.6;
    let base = mix(vec3<f32>(0.05, 0.05, 0.08), u.accent.rgb, fill);
    return vec4<f32>(base + u.accent.rgb * glow, 1.0);
}
"#,
    ),
    (
        "radial",
        r#"
fn viz(uv: vec2<f32>) -> vec4<f32> {
    let aspect = u.resolution.x / max(u.resolution.y, 1.0);
    let p = (uv - vec2<f32>(0.5, 0.5)) * vec2<f32>(aspect, 1.0) * 2.0;
    let r = length(p);
    // Angle from straight down, mirrored left/right so both halves match: bass at the bottom, treble at the top.
    let level = spectrum_at(abs(atan2(p.x, p.y)) / 3.1415927);
    let ring = 0.35 + u.rms * 0.25 + level * 0.3;
    let d = abs(r - ring);
    let line = smoothstep(0.03, 0.0, d);
    let glow = exp(-d * 9.0) * 0.5;
    let core = exp(-r * 3.0) * u.peak * 0.6;
    let col = u.accent.rgb * (line + glow + core);
    return vec4<f32>(vec3<f32>(0.03, 0.03, 0.06) + col, 1.0);
}
"#,
    ),
    (
        "tunnel",
        r#"
fn viz(uv: vec2<f32>) -> vec4<f32> {
    let aspect = u.resolution.x / max(u.resolution.y, 1.0);
    let p = (uv - vec2<f32>(0.5, 0.5)) * vec2<f32>(aspect, 1.0) * 2.0;
    let r = max(length(p), 0.001);
    let depth = 1.0 / r;
    // Rings stream towards the viewer; louder music speeds them up.
    let rings = sin((depth * 7.0) - (u.time * (2.0 + u.rms * 8.0)));
    let band = smoothstep(0.55, 1.0, rings);
    let level = spectrum_at(abs(atan2(p.x, p.y)) / 3.1415927);
    // The rings crowd together towards the middle and alias into noise there, so fade them out.
    let fade = smoothstep(0.12, 0.65, r);
    let col = u.accent.rgb * band * (0.35 + level) * fade;
    return vec4<f32>(vec3<f32>(0.02, 0.02, 0.04) + col, 1.0);
}
"#,
    ),
];

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct VizUniform {
    resolution: [f32; 2],
    time: f32,
    rms: f32,
    peak: f32,
    bin_count: f32,
    pad: [f32; 2],
    accent: [f32; 4],
}

fn pad_bytes_per_row(unpadded: u32) -> u32 {
    ((unpadded + COPY_BYTES_PER_ROW_ALIGNMENT - 1) / COPY_BYTES_PER_ROW_ALIGNMENT) * COPY_BYTES_PER_ROW_ALIGNMENT
}

/// Clamps to a finite 0..=1.25 so a NaN or runaway level can't poison the image.
fn sanitize_level(value: f32) -> f32 {
    if value.is_finite() {
        value.clamp(0.0, 1.25)
    } else {
        0.0
    }
}

pub struct VizRenderer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    pipeline: wgpu::RenderPipeline,
    pipeline_layout: wgpu::PipelineLayout,
    uniform_buffer: wgpu::Buffer,
    spectrum_texture: wgpu::Texture,
    bind_group: wgpu::BindGroup,
    color_texture: wgpu::Texture,
    readback_buffer: wgpu::Buffer,
    width: u32,
    height: u32,
    padded_bytes_per_row: u32,
    unpadded_bytes_per_row: u32,
    uniform: VizUniform,
    bins: Vec<f32>,
    pixels: Vec<u8>,
    last_error: String,
}

impl VizRenderer {
    pub fn new(width: u32, height: u32) -> Option<Self> {
        let width = width.clamp(1, MAX_DIMENSION);
        let height = height.clamp(1, MAX_DIMENSION);

        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
            backends: wgpu::Backends::PRIMARY,
            ..wgpu::InstanceDescriptor::new_without_display_handle()
        });
        let adapter = pollster::block_on(instance.request_adapter(&wgpu::RequestAdapterOptions {
            power_preference: wgpu::PowerPreference::HighPerformance,
            compatible_surface: None,
            force_fallback_adapter: false,
            apply_limit_buckets: false,
        }))
        .ok()?;
        let (device, queue) = pollster::block_on(adapter.request_device(&wgpu::DeviceDescriptor {
            label: Some("spectralis-viz-device"),
            required_features: wgpu::Features::empty(),
            required_limits: wgpu::Limits::downlevel_defaults(),
            experimental_features: wgpu::ExperimentalFeatures::disabled(),
            memory_hints: wgpu::MemoryHints::default(),
            trace: wgpu::Trace::Off,
        }))
        .ok()?;

        let color_texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("spectralis-viz-color"),
            size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: COLOR_FORMAT,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
            view_formats: &[],
        });

        let uniform_buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("spectralis-viz-uniform"),
            size: mem::size_of::<VizUniform>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });

        let spectrum_texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("spectralis-viz-spectrum"),
            size: wgpu::Extent3d { width: MAX_BINS, height: 1, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::R32Float,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });

        let bind_group_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("spectralis-viz-bgl"),
            entries: &[
                wgpu::BindGroupLayoutEntry {
                    binding: 0,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Uniform,
                        has_dynamic_offset: false,
                        min_binding_size: None,
                    },
                    count: None,
                },
                wgpu::BindGroupLayoutEntry {
                    binding: 1,
                    visibility: wgpu::ShaderStages::FRAGMENT,
                    // R32Float can't be filtered without an optional feature; the shader interpolates itself.
                    ty: wgpu::BindingType::Texture {
                        sample_type: wgpu::TextureSampleType::Float { filterable: false },
                        view_dimension: wgpu::TextureViewDimension::D2,
                        multisampled: false,
                    },
                    count: None,
                },
            ],
        });

        let spectrum_view = spectrum_texture.create_view(&wgpu::TextureViewDescriptor::default());
        let bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("spectralis-viz-bg"),
            layout: &bind_group_layout,
            entries: &[
                wgpu::BindGroupEntry { binding: 0, resource: uniform_buffer.as_entire_binding() },
                wgpu::BindGroupEntry { binding: 1, resource: wgpu::BindingResource::TextureView(&spectrum_view) },
            ],
        });

        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("spectralis-viz-pipeline-layout"),
            bind_group_layouts: &[Some(&bind_group_layout)],
            immediate_size: 0,
        });

        let unpadded_bytes_per_row = width * 4;
        let padded_bytes_per_row = pad_bytes_per_row(unpadded_bytes_per_row);
        let readback_buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("spectralis-viz-readback"),
            size: (padded_bytes_per_row * height) as u64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });

        // Start on the first built-in so a freshly created host always draws something.
        let pipeline = Self::build_pipeline(&device, &pipeline_layout, BUILTINS[0].1).ok()?;

        Some(Self {
            device,
            queue,
            pipeline,
            pipeline_layout,
            uniform_buffer,
            spectrum_texture,
            bind_group,
            color_texture,
            readback_buffer,
            width,
            height,
            padded_bytes_per_row,
            unpadded_bytes_per_row,
            uniform: VizUniform {
                resolution: [width as f32, height as f32],
                time: 0.0,
                rms: 0.0,
                peak: 0.0,
                bin_count: 1.0,
                pad: [0.0; 2],
                accent: [1.0, 0.31, 0.10, 1.0],
            },
            bins: vec![0.0; MAX_BINS as usize],
            pixels: vec![0u8; (unpadded_bytes_per_row * height) as usize],
            last_error: String::new(),
        })
    }

    /// Compiles `viz_fn` (the user's `fn viz`) with the standard prelude. Any validation error comes back
    /// as text instead of panicking, and nothing that is currently running is touched.
    fn build_pipeline(
        device: &wgpu::Device,
        layout: &wgpu::PipelineLayout,
        viz_fn: &str,
    ) -> Result<wgpu::RenderPipeline, String> {
        let source = format!("{PRELUDE}\n{viz_fn}");
        let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);

        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("spectralis-viz-shader"),
            source: wgpu::ShaderSource::Wgsl(source.into()),
        });
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("spectralis-viz-pipeline"),
            layout: Some(layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs_main"),
                buffers: &[],
                compilation_options: wgpu::PipelineCompilationOptions::default(),
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs_main"),
                targets: &[Some(wgpu::ColorTargetState {
                    format: COLOR_FORMAT,
                    blend: Some(wgpu::BlendState::REPLACE),
                    write_mask: wgpu::ColorWrites::ALL,
                })],
                compilation_options: wgpu::PipelineCompilationOptions::default(),
            }),
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            multiview_mask: None,
            cache: None,
        });

        match pollster::block_on(scope.pop()) {
            Some(error) => Err(error.to_string()),
            None => Ok(pipeline),
        }
    }

    /// Replaces the running visualizer with `wgsl` (a `fn viz(uv: vec2<f32>) -> vec4<f32>` and any helpers).
    /// On failure the old visualizer keeps running and `last_error` says why.
    pub fn set_shader(&mut self, wgsl: &str) -> bool {
        if wgsl.trim().is_empty() {
            self.last_error = "The shader is empty.".to_string();
            return false;
        }
        if wgsl.len() > MAX_SHADER_BYTES {
            self.last_error = format!("The shader is larger than {MAX_SHADER_BYTES} bytes.");
            return false;
        }

        match Self::build_pipeline(&self.device, &self.pipeline_layout, wgsl) {
            Ok(pipeline) => {
                self.pipeline = pipeline;
                self.last_error.clear();
                true
            }
            Err(message) => {
                self.last_error = message;
                false
            }
        }
    }

    pub fn use_builtin(&mut self, index: usize) -> bool {
        match BUILTINS.get(index) {
            Some((_, source)) => self.set_shader(source),
            None => {
                self.last_error = format!("There is no built-in visualizer {index}.");
                false
            }
        }
    }

    /// Latest audio features. `levels` are spectrum bins (any count up to `MAX_BINS`; more are dropped).
    pub fn set_audio(&mut self, levels: &[f32], rms: f32, peak: f32) {
        let count = levels.len().min(MAX_BINS as usize);
        for (slot, level) in self.bins.iter_mut().zip(levels.iter().take(count)) {
            *slot = sanitize_level(*level);
        }
        for slot in self.bins.iter_mut().skip(count) {
            *slot = 0.0;
        }
        self.uniform.bin_count = count.max(1) as f32;
        self.uniform.rms = sanitize_level(rms);
        self.uniform.peak = sanitize_level(peak);
    }

    pub fn set_accent(&mut self, r: f32, g: f32, b: f32) {
        self.uniform.accent = [r.clamp(0.0, 1.0), g.clamp(0.0, 1.0), b.clamp(0.0, 1.0), 1.0];
    }

    pub fn render(&mut self, time_seconds: f32) -> bool {
        self.uniform.time = if time_seconds.is_finite() { time_seconds } else { 0.0 };
        self.queue.write_buffer(&self.uniform_buffer, 0, bytemuck::bytes_of(&self.uniform));
        self.queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture: &self.spectrum_texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            bytemuck::cast_slice(&self.bins),
            wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(MAX_BINS * 4), rows_per_image: Some(1) },
            wgpu::Extent3d { width: MAX_BINS, height: 1, depth_or_array_layers: 1 },
        );

        let color_view = self.color_texture.create_view(&wgpu::TextureViewDescriptor::default());
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor { label: Some("spectralis-viz-encoder") });
        {
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("spectralis-viz-pass"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &color_view,
                    resolve_target: None,
                    ops: wgpu::Operations {
                        load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                        store: wgpu::StoreOp::Store,
                    },
                    depth_slice: None,
                })],
                depth_stencil_attachment: None,
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &self.bind_group, &[]);
            pass.draw(0..3, 0..1);
        }

        encoder.copy_texture_to_buffer(
            wgpu::TexelCopyTextureInfo {
                texture: &self.color_texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            wgpu::TexelCopyBufferInfo {
                buffer: &self.readback_buffer,
                layout: wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(self.padded_bytes_per_row),
                    rows_per_image: Some(self.height),
                },
            },
            wgpu::Extent3d { width: self.width, height: self.height, depth_or_array_layers: 1 },
        );
        self.queue.submit(Some(encoder.finish()));

        let slice = self.readback_buffer.slice(..);
        let (tx, rx) = std::sync::mpsc::channel();
        slice.map_async(wgpu::MapMode::Read, move |result| {
            let _ = tx.send(result);
        });
        if self.device.poll(wgpu::PollType::wait_indefinitely()).is_err() {
            return false;
        }
        let Ok(Ok(())) = rx.recv() else { return false };

        let copied = {
            let Ok(data) = slice.get_mapped_range() else { return false };
            for row in 0..self.height as usize {
                let src = row * self.padded_bytes_per_row as usize;
                let dst = row * self.unpadded_bytes_per_row as usize;
                self.pixels[dst..dst + self.unpadded_bytes_per_row as usize]
                    .copy_from_slice(&data[src..src + self.unpadded_bytes_per_row as usize]);
            }
            true
        };
        self.readback_buffer.unmap();
        copied
    }

    pub fn pixels(&self) -> &[u8] {
        &self.pixels
    }

    pub fn last_error(&self) -> &str {
        &self.last_error
    }

    pub fn width(&self) -> u32 {
        self.width
    }

    pub fn height(&self) -> u32 {
        self.height
    }
}

// ── C ABI ────────────────────────────────────────────────────────────────────────────────────────

/// Creates a visualizer renderer of `width`x`height`, starting on the first built-in. Returns null when
/// no usable GPU adapter exists, so callers must null-check and fall back to the CPU visualizers.
#[no_mangle]
pub extern "C" fn wgpu_viz_create(width: u32, height: u32) -> *mut c_void {
    match VizRenderer::new(width, height) {
        Some(renderer) => Box::into_raw(Box::new(renderer)) as *mut c_void,
        None => std::ptr::null_mut(),
    }
}

#[no_mangle]
pub extern "C" fn wgpu_viz_destroy(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }
    unsafe { drop(Box::from_raw(handle as *mut VizRenderer)) }
}

/// Number of built-in visualizers; valid indices for `wgpu_viz_use_builtin` are `0..count`.
#[no_mangle]
pub extern "C" fn wgpu_viz_builtin_count() -> u32 {
    BUILTINS.len() as u32
}

#[no_mangle]
pub extern "C" fn wgpu_viz_use_builtin(handle: *mut c_void, index: u32) -> bool {
    if handle.is_null() {
        return false;
    }
    unsafe { (&mut *(handle as *mut VizRenderer)).use_builtin(index as usize) }
}

/// Compiles UTF-8 WGSL containing `fn viz(uv: vec2<f32>) -> vec4<f32>`. False means rejected (bad
/// pointer, not UTF-8, too large, or a compile error): the previous visualizer keeps running and
/// `wgpu_viz_last_error` explains.
#[no_mangle]
pub extern "C" fn wgpu_viz_set_shader(handle: *mut c_void, wgsl_ptr: *const u8, wgsl_len: usize) -> bool {
    if handle.is_null() || wgsl_ptr.is_null() {
        return false;
    }
    let renderer = unsafe { &mut *(handle as *mut VizRenderer) };
    if wgsl_len > MAX_SHADER_BYTES {
        return renderer.set_shader(&" ".repeat(MAX_SHADER_BYTES + 1));
    }
    let bytes = unsafe { std::slice::from_raw_parts(wgsl_ptr, wgsl_len) };
    match std::str::from_utf8(bytes) {
        Ok(text) => renderer.set_shader(text),
        Err(_) => {
            renderer.last_error = "The shader is not valid UTF-8.".to_string();
            false
        }
    }
}

/// Points `out_ptr`/`out_len` at the last compile error (UTF-8, empty when the last shader was accepted).
/// Valid until the next `set_shader`/`use_builtin`/`destroy` on this handle.
#[no_mangle]
pub extern "C" fn wgpu_viz_last_error(handle: *mut c_void, out_ptr: *mut *const u8, out_len: *mut usize) -> bool {
    if handle.is_null() || out_ptr.is_null() || out_len.is_null() {
        return false;
    }
    let renderer = unsafe { &*(handle as *mut VizRenderer) };
    unsafe {
        *out_ptr = renderer.last_error.as_ptr();
        *out_len = renderer.last_error.len();
    }
    true
}

/// Uploads the latest audio features: `count` spectrum bins (clamped to `MAX_BINS`) plus RMS and peak.
#[no_mangle]
pub extern "C" fn wgpu_viz_set_audio(handle: *mut c_void, levels_ptr: *const f32, count: u32, rms: f32, peak: f32) -> bool {
    if handle.is_null() || (levels_ptr.is_null() && count > 0) {
        return false;
    }
    let renderer = unsafe { &mut *(handle as *mut VizRenderer) };
    let levels: &[f32] = if count == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(levels_ptr, count.min(MAX_BINS) as usize) }
    };
    renderer.set_audio(levels, rms, peak);
    true
}

#[no_mangle]
pub extern "C" fn wgpu_viz_set_accent(handle: *mut c_void, r: f32, g: f32, b: f32) -> bool {
    if handle.is_null() {
        return false;
    }
    unsafe { (&mut *(handle as *mut VizRenderer)).set_accent(r, g, b) };
    true
}

#[no_mangle]
pub extern "C" fn wgpu_viz_render(handle: *mut c_void, time_seconds: f32) -> bool {
    if handle.is_null() {
        return false;
    }
    unsafe { (&mut *(handle as *mut VizRenderer)).render(time_seconds) }
}

/// Points at the tightly-packed RGBA8 pixels of the last frame. Valid until the next `render`/`destroy`.
#[no_mangle]
pub extern "C" fn wgpu_viz_pixels(handle: *mut c_void, out_ptr: *mut *const u8, out_len: *mut usize) -> bool {
    if handle.is_null() || out_ptr.is_null() || out_len.is_null() {
        return false;
    }
    let renderer = unsafe { &*(handle as *mut VizRenderer) };
    unsafe {
        *out_ptr = renderer.pixels.as_ptr();
        *out_len = renderer.pixels.len();
    }
    true
}

#[no_mangle]
pub extern "C" fn wgpu_viz_width(handle: *mut c_void) -> u32 {
    if handle.is_null() {
        return 0;
    }
    unsafe { (&*(handle as *mut VizRenderer)).width }
}

#[no_mangle]
pub extern "C" fn wgpu_viz_height(handle: *mut c_void) -> u32 {
    if handle.is_null() {
        return 0;
    }
    unsafe { (&*(handle as *mut VizRenderer)).height }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A renderer, or None (test skips) on machines with no GPU adapter, same policy as tests/smoke.rs.
    fn renderer(width: u32, height: u32) -> Option<VizRenderer> {
        let r = VizRenderer::new(width, height);
        if r.is_none() {
            eprintln!("skipping: no compatible GPU adapter on this machine");
        }
        r
    }

    fn bright_pixels(r: &VizRenderer) -> usize {
        r.pixels().chunks_exact(4).filter(|p| p[0] as u32 + p[1] as u32 + p[2] as u32 > 120).count()
    }

    fn loud_spectrum() -> Vec<f32> {
        (0..64).map(|i| 0.4 + 0.6 * ((i as f32 * 0.37).sin().abs())).collect()
    }

    #[test]
    fn every_builtin_compiles_and_draws() {
        let Some(mut r) = renderer(96, 64) else { return };
        r.set_audio(&loud_spectrum(), 0.5, 0.9);

        for (index, (name, _)) in BUILTINS.iter().enumerate() {
            assert!(r.use_builtin(index), "built-in {name} failed to compile: {}", r.last_error());
            assert!(r.render(1.5), "built-in {name} failed to render");
            assert_eq!(r.pixels().len(), 96 * 64 * 4);
            assert!(bright_pixels(&r) > 20, "built-in {name} drew an (almost) black frame");
            assert!(r.pixels().chunks_exact(4).all(|p| p[3] == 255), "alpha must be opaque for {name}");
        }
    }

    #[test]
    fn output_follows_the_audio() {
        let Some(mut r) = renderer(96, 64) else { return };
        assert!(r.use_builtin(0)); // bars

        r.set_audio(&vec![0.0; 64], 0.0, 0.0);
        assert!(r.render(0.0));
        let silent = r.pixels().to_vec();

        r.set_audio(&vec![1.0; 64], 0.8, 1.0);
        assert!(r.render(0.0));
        let loud = r.pixels().to_vec();

        assert_ne!(silent, loud, "loud audio must change the picture");
        let lit = |frame: &[u8]| frame.chunks_exact(4).filter(|p| p[0] as u32 + p[1] as u32 + p[2] as u32 > 120).count();
        assert!(lit(&loud) > lit(&silent) * 2 + 20, "bars should fill with a loud spectrum");
    }

    #[test]
    fn time_animates_the_tunnel() {
        let Some(mut r) = renderer(64, 64) else { return };
        let tunnel = BUILTINS.iter().position(|(n, _)| *n == "tunnel").unwrap();
        assert!(r.use_builtin(tunnel));
        r.set_audio(&loud_spectrum(), 0.5, 0.5);

        assert!(r.render(0.0));
        let a = r.pixels().to_vec();
        assert!(r.render(0.37));
        let b = r.pixels().to_vec();

        assert_ne!(a, b);
    }

    #[test]
    fn a_custom_shader_runs_and_sees_the_uniforms() {
        let Some(mut r) = renderer(32, 32) else { return };
        // Solid fill: red scales with rms, green with peak, blue is the accent's blue.
        assert!(r.set_shader("fn viz(uv: vec2<f32>) -> vec4<f32> { return vec4<f32>(u.rms, u.peak, u.accent.b, 1.0); }"));
        r.set_accent(0.0, 0.0, 1.0);
        r.set_audio(&[], 1.0, 0.0);
        assert!(r.render(0.0));

        let px = &r.pixels()[..4];
        assert_eq!((px[0], px[1], px[2]), (255, 0, 255));
    }

    #[test]
    fn a_broken_shader_is_rejected_with_a_message_and_the_old_one_keeps_running() {
        let Some(mut r) = renderer(48, 48) else { return };
        r.set_audio(&loud_spectrum(), 0.5, 0.5);
        assert!(r.use_builtin(0));
        assert!(r.render(0.0));
        let before = r.pixels().to_vec();

        assert!(!r.set_shader("fn viz(uv: vec2<f32>) -> vec4<f32> { return this_does_not_exist; }"));
        assert!(!r.last_error().is_empty(), "the compile error should be reported");

        assert!(r.render(0.0));
        assert_eq!(before, r.pixels(), "the previous visualizer must be untouched");

        // A good shader afterwards clears the error.
        assert!(r.set_shader("fn viz(uv: vec2<f32>) -> vec4<f32> { return vec4<f32>(uv, 0.0, 1.0); }"));
        assert!(r.last_error().is_empty());
    }

    #[test]
    fn missing_entry_function_and_wrong_signature_are_compile_errors() {
        let Some(mut r) = renderer(16, 16) else { return };

        assert!(!r.set_shader("fn something_else() -> f32 { return 1.0; }"));
        assert!(!r.set_shader("fn viz(uv: vec2<f32>) -> f32 { return 1.0; }"));
        assert!(!r.last_error().is_empty());
    }

    #[test]
    fn empty_and_oversized_shaders_are_refused_without_touching_the_gpu_pipeline() {
        let Some(mut r) = renderer(16, 16) else { return };

        assert!(!r.set_shader("   \n "));
        assert!(r.last_error().contains("empty"));
        assert!(!r.set_shader(&"a".repeat(MAX_SHADER_BYTES + 1)));
        assert!(r.last_error().contains("larger"));
        assert!(!r.use_builtin(999));
        assert!(r.render(0.0), "still renders with the original pipeline");
    }

    #[test]
    fn hostile_audio_values_cannot_poison_the_frame() {
        let Some(mut r) = renderer(48, 48) else { return };
        assert!(r.use_builtin(0));
        let nasty = [f32::NAN, f32::INFINITY, f32::NEG_INFINITY, -5.0, 1e30];
        r.set_audio(&nasty, f32::NAN, f32::INFINITY);

        assert!(r.render(f32::NAN));
        assert_eq!(r.pixels().len(), 48 * 48 * 4);

        // More bins than the texture holds are dropped, not written out of bounds.
        let too_many = vec![0.5f32; MAX_BINS as usize * 3];
        r.set_audio(&too_many, 0.5, 0.5);
        assert!(r.render(0.0));
    }

    #[test]
    fn dimensions_are_clamped_and_pixel_buffer_matches() {
        let Some(r) = renderer(0, 0) else { return };
        assert_eq!((r.width(), r.height()), (1, 1));
        assert_eq!(r.pixels().len(), 4);

        let Some(odd) = renderer(101, 37) else { return };
        assert_eq!(odd.pixels().len(), 101 * 37 * 4); // row padding must not leak into the buffer
    }

    #[test]
    fn c_abi_round_trip_and_null_safety() {
        assert!(wgpu_viz_builtin_count() >= 3);
        assert!(!wgpu_viz_render(std::ptr::null_mut(), 0.0));
        assert!(!wgpu_viz_set_audio(std::ptr::null_mut(), std::ptr::null(), 0, 0.0, 0.0));
        assert!(!wgpu_viz_use_builtin(std::ptr::null_mut(), 0));
        assert_eq!(wgpu_viz_width(std::ptr::null_mut()), 0);
        wgpu_viz_destroy(std::ptr::null_mut());

        let handle = wgpu_viz_create(40, 30);
        if handle.is_null() {
            eprintln!("skipping: no compatible GPU adapter on this machine");
            return;
        }
        assert_eq!((wgpu_viz_width(handle), wgpu_viz_height(handle)), (40, 30));

        let levels = [0.2f32, 0.9, 0.4, 0.7];
        assert!(wgpu_viz_set_audio(handle, levels.as_ptr(), levels.len() as u32, 0.5, 0.8));
        assert!(wgpu_viz_set_accent(handle, 0.1, 0.8, 0.4));
        assert!(wgpu_viz_use_builtin(handle, 1));
        assert!(wgpu_viz_render(handle, 2.0));

        let mut ptr: *const u8 = std::ptr::null();
        let mut len = 0usize;
        assert!(wgpu_viz_pixels(handle, &mut ptr, &mut len));
        assert_eq!(len, 40 * 30 * 4);

        let bad = b"fn viz(uv: vec2<f32>) -> vec4<f32> { return nope; }";
        assert!(!wgpu_viz_set_shader(handle, bad.as_ptr(), bad.len()));
        let mut err_ptr: *const u8 = std::ptr::null();
        let mut err_len = 0usize;
        assert!(wgpu_viz_last_error(handle, &mut err_ptr, &mut err_len));
        assert!(err_len > 0);
        let message = unsafe { std::str::from_utf8(std::slice::from_raw_parts(err_ptr, err_len)).unwrap() };
        assert!(!message.is_empty());

        let not_utf8 = [0xffu8, 0xfe, 0xfd];
        assert!(!wgpu_viz_set_shader(handle, not_utf8.as_ptr(), not_utf8.len()));

        wgpu_viz_destroy(handle);
    }
}
