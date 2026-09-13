//! Offscreen wgpu renderer for Album Worlds, exposed over a C ABI.
//!
//! Scope (Phase 2 test world): a single rotating/orbit-camera cube scene,
//! proving the full pipeline — device init, depth-tested 3D geometry, a
//! host-driven camera, and CPU readback for compositing into Avalonia —
//! end to end. Not a general scene graph; that is explicit follow-up work
//! tracked in the authoring SDK, not this crate.

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
    color: [f32; 3],
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct CameraUniform {
    view_proj: [[f32; 4]; 4],
}

const CUBE_VERTICES: &[Vertex] = &[
    Vertex { position: [-0.5, -0.5, -0.5], color: [0.9, 0.2, 0.2] },
    Vertex { position: [0.5, -0.5, -0.5], color: [0.2, 0.9, 0.2] },
    Vertex { position: [0.5, 0.5, -0.5], color: [0.2, 0.2, 0.9] },
    Vertex { position: [-0.5, 0.5, -0.5], color: [0.9, 0.9, 0.2] },
    Vertex { position: [-0.5, -0.5, 0.5], color: [0.9, 0.2, 0.9] },
    Vertex { position: [0.5, -0.5, 0.5], color: [0.2, 0.9, 0.9] },
    Vertex { position: [0.5, 0.5, 0.5], color: [0.9, 0.6, 0.2] },
    Vertex { position: [-0.5, 0.5, 0.5], color: [0.5, 0.5, 0.9] },
];

#[rustfmt::skip]
const CUBE_INDICES: &[u16] = &[
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

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec3<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) color: vec3<f32>,
};

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.clip_position = camera.view_proj * vec4<f32>(in.position, 1.0);
    out.color = in.color;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    return vec4<f32>(in.color, 1.0);
}
"#;

/// Bytes-per-row must be a multiple of this for `copy_texture_to_buffer`.
const COPY_BYTES_PER_ROW_ALIGNMENT: u32 = 256;

struct WorldRenderer {
    device: wgpu::Device,
    queue: wgpu::Queue,
    pipeline: wgpu::RenderPipeline,
    vertex_buffer: wgpu::Buffer,
    index_buffer: wgpu::Buffer,
    index_count: u32,
    uniform_buffer: wgpu::Buffer,
    bind_group: wgpu::BindGroup,
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

        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("spectralis-world-pipeline-layout"),
            bind_group_layouts: &[Some(&bind_group_layout)],
            immediate_size: 0,
        });

        let vertex_layout = wgpu::VertexBufferLayout {
            array_stride: mem::size_of::<Vertex>() as u64,
            step_mode: wgpu::VertexStepMode::Vertex,
            attributes: &[
                wgpu::VertexAttribute { format: wgpu::VertexFormat::Float32x3, offset: 0, shader_location: 0 },
                wgpu::VertexAttribute {
                    format: wgpu::VertexFormat::Float32x3,
                    offset: mem::size_of::<[f32; 3]>() as u64,
                    shader_location: 1,
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
            color_texture,
            depth_view,
            readback_buffer,
            width,
            height,
            padded_bytes_per_row,
            unpadded_bytes_per_row,
            pixels: vec![0u8; (unpadded_bytes_per_row * height) as usize],
        })
    }

    /// Renders one frame of the test scene: a cube spinning over time, viewed
    /// through a host-driven orbit camera (yaw/pitch/distance, radians/world units).
    fn render(&mut self, time_seconds: f32, cam_yaw: f32, cam_pitch: f32, cam_dist: f32) -> bool {
        let aspect = self.width as f32 / self.height as f32;
        let proj = perspective(45f32.to_radians(), aspect, 0.1, 100.0);

        let cam_dist = cam_dist.max(0.5);
        let eye = Vec3::new(
            cam_dist * cam_pitch.cos() * cam_yaw.sin(),
            cam_dist * cam_pitch.sin(),
            cam_dist * cam_pitch.cos() * cam_yaw.cos(),
        );
        let view = look_at_mat4(eye, Vec3::ZERO, Vec3::Y);

        let model = Mat4::from_rotation_y(time_seconds * 0.8) * Mat4::from_rotation_x(time_seconds * 0.35);
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
            pass.set_vertex_buffer(0, self.vertex_buffer.slice(..));
            pass.set_index_buffer(self.index_buffer.slice(..), wgpu::IndexFormat::Uint16);
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

/// Renders one frame. Returns false on failure (readback timeout/error); the
/// previous frame's pixels (if any) remain available via `wgpu_host_pixels`.
#[no_mangle]
pub extern "C" fn wgpu_host_render(
    handle: *mut c_void,
    time_seconds: f32,
    cam_yaw: f32,
    cam_pitch: f32,
    cam_dist: f32,
) -> bool {
    if handle.is_null() {
        return false;
    }
    let renderer = unsafe { &mut *(handle as *mut WorldRenderer) };
    renderer.render(time_seconds, cam_yaw, cam_pitch, cam_dist)
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

