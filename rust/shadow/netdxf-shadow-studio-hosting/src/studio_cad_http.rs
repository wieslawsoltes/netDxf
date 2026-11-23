#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioCadHttp;

impl StudioCadHttp {
    pub fn routes() -> &'static [&'static str] {
        &["/api/cad/snapshot", "/api/cad/render", "/api/cad/pick"]
    }

    pub fn map_cad_workspace() -> &'static [&'static str] {
        Self::routes()
    }
}

