//! Offscreen wgpu renderer for Album Worlds, exposed over a C ABI.
//!
//! Scope (Phase 2 test world, now with a texture atlas): a spinning built-in cube plus a
//! positional first-person camera a guest world drives by calling `set_camera_pose` every
//! frame, proving the full pipeline — device init, depth-tested 3D geometry, a guest-driven
//! camera, guest-uploaded texture sampling, and CPU readback for compositing into Avalonia —
//! end to end. Not a general scene graph; a world is still one flat vertex/index buffer plus
//! one shared texture atlas, no per-object transforms/materials. That's explicit follow-up
//! work tracked in the authoring SDK, not this crate.

pub mod viz;

use std::ffi::c_void;
use std::mem;

use bytemuck::{Pod, Zeroable};
use glam::camera::rh::proj::directx::perspective;
use glam::camera::rh::view::look_at_mat4;
use glam::{Mat4, Vec3};
use wgpu::util::DeviceExt;

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct Vertex {
    position: [f32; 3],
    uv: [f32; 2],
    color: [f32; 3],
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct CameraUniform {
    view_proj: [[f32; 4]; 4],
}

#[rustfmt::skip]
const CUBE_VERTICES: &[Vertex] = &[
    Vertex { position: [-0.5, -0.5, -0.5], uv: [0.0, 0.0], color: [0.9, 0.2, 0.2] },
    Vertex { position: [0.5, -0.5, -0.5],  uv: [0.0, 0.0], color: [0.2, 0.9, 0.2] },
    Vertex { position: [0.5, 0.5, -0.5],   uv: [0.0, 0.0], color: [0.2, 0.2, 0.9] },
    Vertex { position: [-0.5, 0.5, -0.5],  uv: [0.0, 0.0], color: [0.9, 0.9, 0.2] },
    Vertex { position: [-0.5, -0.5, 0.5],  uv: [0.0, 0.0], color: [0.9, 0.2, 0.9] },
    Vertex { position: [0.5, -0.5, 0.5],   uv: [0.0, 0.0], color: [0.2, 0.9, 0.9] },
    Vertex { position: [0.5, 0.5, 0.5],    uv: [0.0, 0.0], color: [0.9, 0.6, 0.2] },
    Vertex { position: [-0.5, 0.5, 0.5],   uv: [0.0, 0.0], color: [0.5, 0.5, 0.9] },
];

#[rustfmt::skip]
const CUBE_INDICES: &[u32] = &[
    0, 1, 2, 2, 3, 0, // back
    4, 6, 5, 6, 4, 7, // front
    4, 0, 3, 3, 7, 4, // left
    1, 5, 6, 6, 2, 1, // right
    3, 2, 6, 6, 7, 3, // top
    4, 5, 1, 1, 0, 4, // bottom
];

const SHADER_SRC: &str = r#"
struct CameraUniform {
    view_proj: mat4x4<f32>,
};
@group(0) @binding(0)
var<uniform> camera: CameraUniform;

@group(1) @binding(0)
var atlas_texture: texture_2d<f32>;
@group(1) @binding(1)
var atlas_sampler: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) color: vec3<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec3<f32>,
};

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.clip_position = camera.view_proj * vec4<f32>(in.position, 1.0);
    out.uv = in.uv;
    out.color = in.color;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    // No guest texture submitted yet -> atlas is a 1x1 opaque white pixel, so this multiply
    // is a no-op and the shading is identical to the old flat-vertex-color-only pipeline.
    let sampled = textureSample(atlas_texture, atlas_sampler, in.uv);
    return vec4<f32>(sampled.rgb * in.color, 1.0);
}
"#;

/// Bytes-per-row must be a multiple of this for `copy_texture_to_buffer`.
const COPY_BYTES_PER_ROW_ALIGNMENT: u32 = 256;

/// Caps on guest-submitted geometry (`wgpu_host_set_geometry`) — bounded the same way every
/// other untrusted-wasm-content boundary in this project is (fuel limits, string length caps in
/// WasmWorldHost, ...). Indices are u32 now (see `wgpu_host_set_geometry` doc), so these are
/// picked as a sane ceiling for a real room-sized mesh, not a format limitation — sized with
/// headroom over an actual measured room export (indie_bedroom.blend, full detail: ~211k
/// vertices / ~1.2M indices), not a round-number guess.
const MAX_VERTICES: u32 = 500_000;
const MAX_INDICES: u32 = 1_500_000;

/// Cap on a guest-submitted texture atlas (`wgpu_host_set_texture`) in either dimension — bounds
/// GPU memory for a single RGBA8 upload (4096x4096 RGBA8 is 64MiB, already a generous atlas).
const MAX_TEXTURE_DIM: u32 = 4096;

struct WorldRenderer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    pipeline: wgpu::RenderPipeline,
    vertex_buffer: wgpu::Buffer,
    index_buffer: wgpu::Buffer,
    index_count: u32,
    uniform_buffer: wgpu::Buffer,
    bind_group: wgpu::BindGroup,
    texture_bind_group_layout: wgpu::BindGroupLayout,
    atlas_sampler: wgpu::Sampler,
    /// Kept alive so the `wgpu::TextureView` referenced by `texture_bind_group` stays valid —
    /// replaced wholesale (not written into) on every `set_texture` call.
    atlas_texture: wgpu::Texture,
    texture_bind_group: wgpu::BindGroup,
    color_texture: wgpu::Texture,
    depth_view: wgpu::TextureView,
    readback_buffer: wgpu::Buffer,
    width: u32,
    height: u32,
    padded_bytes_per_row: u32,
    unpadded_bytes_per_row: u32,
    /// Tightly-packed RGBA8 pixels from the most recent `render` call, row-padding
    /// already stripped. Read via `wgpu_host_pixels`; overwritten by the next render.
    pixels: Vec<u8>,
    /// True once `set_geometry` has replaced the built-in cube — a guest's room shouldn't spin
    /// in place the way the demo cube does, so the per-frame model rotation only applies before
    /// any real geometry has been submitted.
    is_guest_geometry: bool,
}

fn pad_bytes_per_row(unpadded: u32) -> u32 {
    let align = COPY_BYTES_PER_ROW_ALIGNMENT;
    ((unpadded + align - 1) / align) * align
}

impl WorldRenderer {
    fn new(width: u32, height: u32) -> Option<Self> {
        let width = width.max(1);
        let height = height.max(1);

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
            label: Some("spectralis-world-device"),
            required_features: wgpu::Features::empty(),
            required_limits: wgpu::Limits::downlevel_defaults(),
            experimental_features: wgpu::ExperimentalFeatures::disabled(),
            memory_hints: wgpu::MemoryHints::default(),
            trace: wgpu::Trace::Off,
        }))
        .ok()?;

        let color_format = wgpu::TextureFormat::Rgba8Unorm;

        let color_texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("spectralis-world-color"),
            size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: color_format,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
            view_formats: &[],
        });
        let depth_texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("spectralis-world-depth"),
            size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Depth32Float,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
            view_formats: &[],
        });
        let depth_view = depth_texture.create_view(&wgpu::TextureViewDescriptor::default());

        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("spectralis-world-shader"),
            source: wgpu::ShaderSource::Wgsl(SHADER_SRC.into()),
        });

        let uniform_buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("spectralis-world-camera"),
            size: mem::size_of::<CameraUniform>() as u64,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });

        let bind_group_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("spectralis-world-bgl"),
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::VERTEX,
                ty: wgpu::BindingType::Buffer {
                    ty: wgpu::BufferBindingType::Uniform,
                    has_dynamic_offset: false,
                    min_binding_size: None,
                },
                count: None,
            }],
        });

        let bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("spectralis-world-bg"),
            layout: &bind_group_layout,
            entries: &[wgpu::BindGroupEntry {
                binding: 0,
                resource: uniform_buffer.as_entire_binding(),
            }],
        });

        let texture_bind_group_layout =
            device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
                label: Some("spectralis-world-texture-bgl"),
                entries: &[
                    wgpu::BindGroupLayoutEntry {
                        binding: 0,
                        visibility: wgpu::ShaderStages::FRAGMENT,
                        ty: wgpu::BindingType::Texture {
                            sample_type: wgpu::TextureSampleType::Float { filterable: true },
                            view_dimension: wgpu::TextureViewDimension::D2,
                            multisampled: false,
                        },
                        count: None,
                    },
                    wgpu::BindGroupLayoutEntry {
                        binding: 1,
                        visibility: wgpu::ShaderStages::FRAGMENT,
                        ty: wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
                        count: None,
                    },
                ],
            });

        let atlas_sampler = device.create_sampler(&wgpu::SamplerDescriptor {
            label: Some("spectralis-world-sampler"),
            address_mode_u: wgpu::AddressMode::ClampToEdge,
            address_mode_v: wgpu::AddressMode::ClampToEdge,
            address_mode_w: wgpu::AddressMode::ClampToEdge,
            mag_filter: wgpu::FilterMode::Linear,
            min_filter: wgpu::FilterMode::Linear,
            mipmap_filter: wgpu::MipmapFilterMode::Nearest,
            ..Default::default()
        });

        // 1x1 opaque white pixel — sampling it is a no-op multiply against the vertex color, so
        // a guest that never calls `set_texture` renders exactly like the old flat-color pipeline.
        let atlas_texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("spectralis-world-atlas-default"),
            size: wgpu::Extent3d { width: 1, height: 1, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture: &atlas_texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            &[255u8, 255, 255, 255],
            wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(4), rows_per_image: Some(1) },
            wgpu::Extent3d { width: 1, height: 1, depth_or_array_layers: 1 },
        );
        let atlas_view = atlas_texture.create_view(&wgpu::TextureViewDescriptor::default());
        let texture_bind_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("spectralis-world-texture-bg"),
            layout: &texture_bind_group_layout,
            entries: &[
                wgpu::BindGroupEntry { binding: 0, resource: wgpu::BindingResource::TextureView(&atlas_view) },
                wgpu::BindGroupEntry { binding: 1, resource: wgpu::BindingResource::Sampler(&atlas_sampler) },
            ],
        });

        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("spectralis-world-pipeline-layout"),
            bind_group_layouts: &[Some(&bind_group_layout), Some(&texture_bind_group_layout)],
            immediate_size: 0,
        });

        let vertex_layout = wgpu::VertexBufferLayout {
            array_stride: mem::size_of::<Vertex>() as u64,
            step_mode: wgpu::VertexStepMode::Vertex,
            attributes: &[
                wgpu::VertexAttribute { format: wgpu::VertexFormat::Float32x3, offset: 0, shader_location: 0 },
                wgpu::VertexAttribute {
                    format: wgpu::VertexFormat::Float32x2,
                    offset: mem::size_of::<[f32; 3]>() as u64,
                    shader_location: 1,
                },
                wgpu::VertexAttribute {
                    format: wgpu::VertexFormat::Float32x3,
                    offset: mem::size_of::<[f32; 3]>() as u64 + mem::size_of::<[f32; 2]>() as u64,
                    shader_location: 2,
                },
            ],
        };

        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("spectralis-world-pipeline"),
            layout: Some(&pipeline_layout),
            vertex: wgpu::VertexState {
                module: &shader,
                entry_point: Some("vs_main"),
                buffers: &[Some(vertex_layout)],
                compilation_options: wgpu::PipelineCompilationOptions::default(),
            },
            fragment: Some(wgpu::FragmentState {
                module: &shader,
                entry_point: Some("fs_main"),
                targets: &[Some(wgpu::ColorTargetState {
                    format: color_format,
                    blend: Some(wgpu::BlendState::REPLACE),
                    write_mask: wgpu::ColorWrites::ALL,
                })],
                compilation_options: wgpu::PipelineCompilationOptions::default(),
            }),
            primitive: wgpu::PrimitiveState {
                topology: wgpu::PrimitiveTopology::TriangleList,
                cull_mode: Some(wgpu::Face::Back),
                ..Default::default()
            },
            depth_stencil: Some(wgpu::DepthStencilState {
                format: wgpu::TextureFormat::Depth32Float,
                depth_write_enabled: Some(true),
                depth_compare: Some(wgpu::CompareFunction::Less),
                stencil: wgpu::StencilState::default(),
                bias: wgpu::DepthBiasState::default(),
            }),
            multisample: wgpu::MultisampleState::default(),
            multiview_mask: None,
            cache: None,
        });

        let vertex_buffer = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("spectralis-world-vbuf"),
            contents: bytemuck::cast_slice(CUBE_VERTICES),
            usage: wgpu::BufferUsages::VERTEX,
        });
        let index_buffer = device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("spectralis-world-ibuf"),
            contents: bytemuck::cast_slice(CUBE_INDICES),
            usage: wgpu::BufferUsages::INDEX,
        });

        let unpadded_bytes_per_row = width * 4;
        let padded_bytes_per_row = pad_bytes_per_row(unpadded_bytes_per_row);
        let readback_buffer = device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("spectralis-world-readback"),
            size: (padded_bytes_per_row * height) as u64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
            mapped_at_creation: false,
        });

        Some(Self {
            device,
            queue,
            pipeline,
            vertex_buffer,
            index_buffer,
            index_count: CUBE_INDICES.len() as u32,
            uniform_buffer,
            bind_group,
            texture_bind_group_layout,
            atlas_sampler,
            atlas_texture,
            texture_bind_group,
            color_texture,
            depth_view,
            readback_buffer,
            width,
            height,
            padded_bytes_per_row,
            unpadded_bytes_per_row,
            pixels: vec![0u8; (unpadded_bytes_per_row * height) as usize],
            is_guest_geometry: false,
        })
    }

    /// Renders one frame through a positional first-person camera: `eye` is world-space
    /// position, `yaw`/`pitch` (radians) give the look direction — there's no orbit target and
    /// no fixed distance, the guest world walks the camera around by calling `set_camera_pose`
    /// every frame (see `WasmWorldHost.Input`/`set_camera_pose`). Before any real geometry is
    /// submitted, the built-in test cube still spins in place over time regardless of camera
    /// position, same as before this camera model changed.
    fn render(&mut self, time_seconds: f32, eye_x: f32, eye_y: f32, eye_z: f32, yaw: f32, pitch: f32) -> bool {
        let aspect = self.width as f32 / self.height as f32;
        let proj = perspective(45f32.to_radians(), aspect, 0.1, 100.0);

        let eye = Vec3::new(eye_x, eye_y, eye_z);
        let forward = Vec3::new(pitch.cos() * yaw.sin(), pitch.sin(), pitch.cos() * yaw.cos());
        let view = look_at_mat4(eye, eye + forward, Vec3::Y);

        let model = if self.is_guest_geometry {
            Mat4::IDENTITY
        } else {
            Mat4::from_rotation_y(time_seconds * 0.8) * Mat4::from_rotation_x(time_seconds * 0.35)
        };
        let view_proj = proj * view * model;

        let uniform = CameraUniform { view_proj: view_proj.to_cols_array_2d() };
        self.queue.write_buffer(&self.uniform_buffer, 0, bytemuck::bytes_of(&uniform));

        let color_view = self.color_texture.create_view(&wgpu::TextureViewDescriptor::default());

        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor { label: Some("spectralis-world-encoder") });

        {
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("spectralis-world-pass"),
                color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                    view: &color_view,
                    resolve_target: None,
                    ops: wgpu::Operations {
                        load: wgpu::LoadOp::Clear(wgpu::Color { r: 0.05, g: 0.05, b: 0.08, a: 1.0 }),
                        store: wgpu::StoreOp::Store,
                    },
                    depth_slice: None,
                })],
                depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                    view: &self.depth_view,
                    depth_ops: Some(wgpu::Operations {
                        load: wgpu::LoadOp::Clear(1.0),
                        store: wgpu::StoreOp::Store,
                    }),
                    stencil_ops: None,
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });

            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &self.bind_group, &[]);
            pass.set_bind_group(1, &self.texture_bind_group, &[]);
            pass.set_vertex_buffer(0, self.vertex_buffer.slice(..));
            pass.set_index_buffer(self.index_buffer.slice(..), wgpu::IndexFormat::Uint32);
            pass.draw_indexed(0..self.index_count, 0, 0..1);
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

        let mapped_ok = {
            let Ok(data) = slice.get_mapped_range() else { return false };
            for row in 0..self.height as usize {
                let src_start = row * self.padded_bytes_per_row as usize;
                let src = &data[src_start..src_start + self.unpadded_bytes_per_row as usize];
                let dst_start = row * self.unpadded_bytes_per_row as usize;
                self.pixels[dst_start..dst_start + self.unpadded_bytes_per_row as usize].copy_from_slice(src);
            }
            true
        };
        self.readback_buffer.unmap();

        mapped_ok
    }

    /// Replaces the current geometry with guest-submitted vertices/indices, mirroring
    /// `Vertex { position: [f32;3], uv: [f32;2], color: [f32;3] }` — a world that never calls
    /// `set_texture` still gets flat per-vertex color (the default atlas is a 1x1 white pixel).
    /// Rejects (returns false, leaves existing geometry untouched) empty or oversized
    /// submissions rather than trying to partially apply them.
    fn set_geometry(&mut self, vertices: &[Vertex], indices: &[u32]) -> bool {
        if vertices.is_empty() || indices.is_empty() {
            return false;
        }
        if vertices.len() as u32 > MAX_VERTICES || indices.len() as u32 > MAX_INDICES {
            return false;
        }

        self.vertex_buffer = self.device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("spectralis-world-vbuf-guest"),
            contents: bytemuck::cast_slice(vertices),
            usage: wgpu::BufferUsages::VERTEX,
        });
        self.index_buffer = self.device.create_buffer_init(&wgpu::util::BufferInitDescriptor {
            label: Some("spectralis-world-ibuf-guest"),
            contents: bytemuck::cast_slice(indices),
            usage: wgpu::BufferUsages::INDEX,
        });
        self.index_count = indices.len() as u32;
        self.is_guest_geometry = true;
        true
    }

    /// Replaces the shared texture atlas guest geometry's UVs sample against. `rgba` must be
    /// exactly `width * height * 4` bytes (tightly packed RGBA8, no row padding — this is a
    /// plain upload, not `wgpu_host_pixels`' readback layout). Rejects (atlas left untouched)
    /// zero dimensions, an oversized dimension, or a length mismatch.
    fn set_texture(&mut self, width: u32, height: u32, rgba: &[u8]) -> bool {
        if width == 0 || height == 0 || width > MAX_TEXTURE_DIM || height > MAX_TEXTURE_DIM {
            return false;
        }
        let expected = width as usize * height as usize * 4;
        if rgba.len() != expected {
            return false;
        }

        let texture = self.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("spectralis-world-atlas-guest"),
            size: wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        self.queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture: &texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            rgba,
            wgpu::TexelCopyBufferLayout { offset: 0, bytes_per_row: Some(width * 4), rows_per_image: Some(height) },
            wgpu::Extent3d { width, height, depth_or_array_layers: 1 },
        );

        let view = texture.create_view(&wgpu::TextureViewDescriptor::default());
        self.texture_bind_group = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("spectralis-world-texture-bg-guest"),
            layout: &self.texture_bind_group_layout,
            entries: &[
                wgpu::BindGroupEntry { binding: 0, resource: wgpu::BindingResource::TextureView(&view) },
                wgpu::BindGroupEntry { binding: 1, resource: wgpu::BindingResource::Sampler(&self.atlas_sampler) },
            ],
        });
        self.atlas_texture = texture;
        true
    }
}

/// Creates a renderer targeting `width`x`height`. Returns null on any device/adapter
/// initialization failure (e.g. no compatible GPU) — callers must null-check.
#[no_mangle]
pub extern "C" fn wgpu_host_create(width: u32, height: u32) -> *mut c_void {
    match WorldRenderer::new(width, height) {
        Some(renderer) => Box::into_raw(Box::new(renderer)) as *mut c_void,
        None => std::ptr::null_mut(),
    }
}

/// Destroys a renderer created by `wgpu_host_create`. `handle` must not be used again.
#[no_mangle]
pub extern "C" fn wgpu_host_destroy(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }
    unsafe {
        drop(Box::from_raw(handle as *mut WorldRenderer));
    }
}

/// Renders one frame through a positional camera (`eye_x/y/z` world position, `yaw`/`pitch`
/// radians look direction — see `WorldRenderer::render`). Returns false on failure (readback
/// timeout/error); the previous frame's pixels (if any) remain available via `wgpu_host_pixels`.
#[no_mangle]
pub extern "C" fn wgpu_host_render(
    handle: *mut c_void,
    time_seconds: f32,
    eye_x: f32,
    eye_y: f32,
    eye_z: f32,
    yaw: f32,
    pitch: f32,
) -> bool {
    if handle.is_null() {
        return false;
    }
    let renderer = unsafe { &mut *(handle as *mut WorldRenderer) };
    renderer.render(time_seconds, eye_x, eye_y, eye_z, yaw, pitch)
}

/// Points `out_ptr`/`out_len` at the tightly-packed RGBA8 pixels from the most
/// recent `wgpu_host_render` call (row-major, no padding). The pointer is only
/// valid until the next `render`/`destroy` call on this handle — callers must
/// copy the bytes out before calling either again.
#[no_mangle]
pub extern "C" fn wgpu_host_pixels(handle: *mut c_void, out_ptr: *mut *const u8, out_len: *mut usize) -> bool {
    if handle.is_null() || out_ptr.is_null() || out_len.is_null() {
        return false;
    }
    let renderer = unsafe { &*(handle as *mut WorldRenderer) };
    unsafe {
        *out_ptr = renderer.pixels.as_ptr();
        *out_len = renderer.pixels.len();
    }
    true
}

/// Replaces the renderer's current geometry with guest-submitted vertices/indices. Vertex
/// layout is `[f32;3] position, [f32;2] uv, [f32;3] color` interleaved (32 bytes/vertex) —
/// `vertex_count` is a vertex count, not a float count. `indices_ptr` is u32 indices,
/// `index_count` an index count. Returns false (geometry unchanged) if either pointer is null,
/// either count is zero, or either count exceeds this renderer's fixed caps — callers must not
/// treat a false return as "geometry cleared", the previous geometry (built-in test cube or an
/// earlier valid submission) stays in place.
#[no_mangle]
pub extern "C" fn wgpu_host_set_geometry(
    handle: *mut c_void,
    vertices_ptr: *const f32,
    vertex_count: u32,
    indices_ptr: *const u32,
    index_count: u32,
) -> bool {
    if handle.is_null() || vertices_ptr.is_null() || indices_ptr.is_null() {
        return false;
    }
    if vertex_count == 0 || index_count == 0 {
        return false;
    }
    if vertex_count > MAX_VERTICES || index_count > MAX_INDICES {
        return false;
    }

    let renderer = unsafe { &mut *(handle as *mut WorldRenderer) };
    let vertices = unsafe { std::slice::from_raw_parts(vertices_ptr as *const Vertex, vertex_count as usize) };
    let indices = unsafe { std::slice::from_raw_parts(indices_ptr, index_count as usize) };
    renderer.set_geometry(vertices, indices)
}

/// Replaces the renderer's shared texture atlas. `rgba_ptr` points at `rgba_len` bytes of
/// tightly-packed RGBA8 pixels (`width * height * 4`, row-major, no padding). Returns false
/// (atlas unchanged) for a null pointer, a zero/oversized dimension, or a length mismatch.
/// Guest geometry's `uv` attribute samples whatever atlas is currently bound — call this before
/// (or in the same frame as) the `set_geometry` call whose UVs are meant to address it.
#[no_mangle]
pub extern "C" fn wgpu_host_set_texture(
    handle: *mut c_void,
    rgba_ptr: *const u8,
    rgba_len: usize,
    width: u32,
    height: u32,
) -> bool {
    if handle.is_null() || rgba_ptr.is_null() {
        return false;
    }

    let renderer = unsafe { &mut *(handle as *mut WorldRenderer) };
    let rgba = unsafe { std::slice::from_raw_parts(rgba_ptr, rgba_len) };
    renderer.set_texture(width, height, rgba)
}

#[no_mangle]
pub extern "C" fn wgpu_host_width(handle: *mut c_void) -> u32 {
    if handle.is_null() {
        return 0;
    }
    unsafe { (&*(handle as *mut WorldRenderer)).width }
}

#[no_mangle]
pub extern "C" fn wgpu_host_height(handle: *mut c_void) -> u32 {
    if handle.is_null() {
        return 0;
    }
    unsafe { (&*(handle as *mut WorldRenderer)).height }
}
