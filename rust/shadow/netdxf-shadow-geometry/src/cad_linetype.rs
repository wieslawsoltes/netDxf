#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadLinetypeDecorationKind {
    #[default]
    Text,
    Shape,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadLinetypeRotation {
    #[default]
    Relative,
    Upright,
    Absolute,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadLinetypeDecoration {
    pub kind: CadLinetypeDecorationKind,
    pub content: String,
    pub resource: String,
    pub rotation: CadLinetypeRotation,
    pub angle: f64,
    pub scale: f64,
    pub offset: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadLinetypeElement {
    pub length: f64,
    pub decoration: Option<CadLinetypeDecoration>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLinetype {
    pub elements: Vec<CadLinetypeElement>,
    pub scale: f64,
    pub normal: Vec3,
}

