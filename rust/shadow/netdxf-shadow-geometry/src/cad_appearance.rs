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

impl CadStrokeGroup {
    pub fn transform(&self, m: Affine3) -> Self {
        Self {
            contours: self
                .contours
                .iter()
                .map(|c| CadContour {
                    curves: c.curves.iter().map(|cv| cv.transform(m)).collect(),
                    closed: c.closed,
                })
                .collect(),
            argb: self.argb,
        }
    }
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

impl CadRegion {
    pub fn transform(&self, m: Affine3) -> Self {
        Self {
            contours: self
                .contours
                .iter()
                .map(|c| CadContour {
                    curves: c.curves.iter().map(|cv| cv.transform(m)).collect(),
                    closed: c.closed,
                })
                .collect(),
            fill_rule: self.fill_rule,
            argb: self.argb,
            gradient: self.gradient.clone(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WidthBoundaryCurve {
    pub centerline: Curve3,
    pub start_width: f64,
    pub end_width: f64,
    pub side: f64,
    pub frame: Affine3,
}

impl WidthBoundaryCurve {
    #[inline]
    pub fn at(&self, t: f64) -> Vec3 {
        self.centerline.evaluate(t)
    }

    pub fn transform(&self, m: Affine3) -> Self {
        Self {
            centerline: self.centerline.transform(m),
            start_width: self.start_width,
            end_width: self.end_width,
            side: self.side,
            frame: m * self.frame,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ReversedCurve {
    pub curve: Curve3,
}

impl ReversedCurve {
    #[inline]
    pub fn at(&self, t: f64) -> Vec3 {
        self.curve.evaluate(1.0 - t)
    }

    #[inline]
    pub fn closed(&self) -> bool {
        self.curve.closed()
    }

    pub fn transform(&self, m: Affine3) -> Self {
        Self {
            curve: self.curve.transform(m),
        }
    }
}

