//! FerroUI-based CAD Studio UI shell replacing `netDxf.Shadow.Studio.Web`.

#![forbid(unsafe_code)]

pub mod ferro_studio_app;
pub mod ferro_studio_panel;
pub mod studio_web_assets;

pub use ferro_studio_app::*;
pub use ferro_studio_panel::*;
pub use studio_web_assets::*;
