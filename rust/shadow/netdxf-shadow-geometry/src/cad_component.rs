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

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadComponentStyle {
    pub value: CadStyle,
    pub color: CadStyleSource,
    pub lineweight: CadStyleSource,
    pub linetype: CadStyleSource,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadComponent {
    pub key: String,
    pub geometry: CadGeometry,
    pub style: Option<CadComponentStyle>,
}

