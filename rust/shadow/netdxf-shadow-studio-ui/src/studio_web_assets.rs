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

/// Static asset descriptors matching `netDxf.Shadow.Studio.Web.StudioWebAssets`.
pub struct StudioWebAssets;

impl StudioWebAssets {
    pub const APP_TITLE: &'static str = "netDxf Shadow Studio (FerroUI + Skia)";
    pub const UI_FRAMEWORK: &'static str = "FerroUI";
    pub const RENDER_BACKEND: &'static str = "Skia";
    pub const SHELL: &'static str = "studio-shell.html";
    pub const DIAGNOSTICS: &'static str = "diagnostics-shell.html";
}

