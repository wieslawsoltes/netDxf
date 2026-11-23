//! Backend-agnostic CAD render compiler, SVG renderer, rich text, and camera (`netDxf.Shadow.Rendering`).

#![forbid(unsafe_code)]

pub mod gradient_plan;
pub mod i_text_layout_engine;
pub mod linetype_coordinates;
pub mod linetype_fitting;
pub mod linetype_paths;
pub mod linetype_planner;
pub mod linetype_render_compiler;
pub mod linetype_transport;
pub mod m_text_color_codec;
pub mod model_annotation_context;
pub mod paper_viewport;
pub mod perspective_camera;
pub mod perspective_item_projection;
pub mod render_backend;
pub mod render_compiler;
pub mod render_hit_testing;
pub mod render_paint;
pub mod render_scene;
pub mod rich_text;
pub mod rich_text_layout;
pub mod rich_text_paragraph_style;
pub mod stroke_pattern;
pub mod svg_render_backend;
pub mod text_layout;
pub mod viewport_layer_overrides;

pub use gradient_plan::*;
pub use i_text_layout_engine::*;
pub use linetype_coordinates::*;
pub use linetype_fitting::*;
pub use linetype_paths::*;
pub use linetype_planner::*;
pub use linetype_render_compiler::*;
pub use linetype_transport::*;
pub use m_text_color_codec::*;
pub use model_annotation_context::*;
pub use paper_viewport::*;
pub use perspective_camera::*;
pub use perspective_item_projection::*;
pub use render_backend::*;
pub use render_compiler::*;
pub use render_hit_testing::*;
pub use render_paint::*;
pub use render_scene::*;
pub use rich_text::*;
pub use rich_text_layout::*;
pub use rich_text_paragraph_style::*;
pub use stroke_pattern::*;
pub use svg_render_backend::*;
pub use text_layout::*;
pub use viewport_layer_overrides::*;
