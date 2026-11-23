#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadFillRule {
    #[default]
    EvenOdd,
    NonZero,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadStrokeGroup {
    pub contours: Vec<CadContour>,
    pub argb: Option<u32>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadGradient {
    pub kind: String,
    pub color1: u32,
    pub color2: u32,
    pub frame: Affine3,
    pub shift: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadRegion {
    pub contours: Vec<CadContour>,
    pub fill_rule: CadFillRule,
    pub argb: Option<u32>,
    pub gradient: Option<CadGradient>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WidthBoundaryCurve {
    pub centerline: Curve3,
    pub start_width: f64,
    pub end_width: f64,
    pub side: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ReversedCurve {
    pub curve: Curve3,
}

