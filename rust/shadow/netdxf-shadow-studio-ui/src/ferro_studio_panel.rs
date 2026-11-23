#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::Vec3;
#[allow(unused_imports)]
use netdxf_shadow_rendering::{IRenderBackend, RenderFrame, RenderImage, Viewport};
#[allow(unused_imports)]
use netdxf_shadow_rendering_skia::SkiaRenderBackend;
#[allow(unused_imports)]
use netdxf_shadow_studio_core::Studio;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};

/// FerroUI dockable panel identifiers for the CAD Studio workspace.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum FerroStudioPanel {
    Viewport,
    Layers,
    Properties,
    Symbols,
    Health,
    AgentChat,
}

