//! Skia rendering & text layout backend replacing `SkiaSharp` (`netDxf.Shadow.Rendering.SkiaSharp`).

#![forbid(unsafe_code)]

pub mod skia_render_backend;
pub mod skia_text_layout_engine;

pub use skia_render_backend::{ISkiaImageResolver, IVersionedSkiaImageResolver, SkiaRenderBackend};
pub use skia_text_layout_engine::{SkiaTextCacheStatistics, SkiaTextLayoutEngine};
