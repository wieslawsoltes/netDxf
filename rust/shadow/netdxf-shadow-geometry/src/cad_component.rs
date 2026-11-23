#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadStyleSource {
    #[default]
    Explicit,
    ByLayer,
    ByBlock,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadComponentStyle {
    pub value: CadStyle,
    pub color: CadStyleSource,
    pub lineweight: CadStyleSource,
    pub linetype: CadStyleSource,
    pub alpha: Option<u8>,
    pub dashes: Vec<f64>,
    pub dash_scale: f64,
    pub inherit_layer: bool,
}

impl CadComponentStyle {
    pub fn resolve(&self, parent: &CadStyle) -> CadStyle {
        let mut out = self.value.clone();
        if self.inherit_layer && out.layer.is_empty() {
            out.layer = parent.layer.clone();
        }
        out
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadComponent {
    pub key: String,
    pub geometry: CadGeometry,
    pub style: Option<CadComponentStyle>,
}

