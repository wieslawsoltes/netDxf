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
    pub big_font: String,
    pub font_family: String,
    pub shape_number: i16,
    pub rotation: CadLinetypeRotation,
    pub angle: f64,
    pub scale: f64,
    pub offset: Vec3,
    pub style_height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub is_backward: bool,
    pub is_upside_down: bool,
    pub is_vertical: bool,
}

impl CadLinetypeDecoration {
    #[inline]
    pub fn rotation_type(&self) -> CadLinetypeRotation {
        self.rotation
    }

    pub fn create_text(
        content: impl Into<String>,
        resource: impl Into<String>,
        scale: f64,
        offset: Vec3,
    ) -> Self {
        Self {
            kind: CadLinetypeDecorationKind::Text,
            content: content.into(),
            resource: resource.into(),
            big_font: String::new(),
            font_family: String::new(),
            shape_number: 0,
            rotation: CadLinetypeRotation::Upright,
            angle: 0.0,
            scale,
            offset,
            style_height: 0.0,
            width_factor: 1.0,
            oblique_angle: 0.0,
            is_backward: false,
            is_upside_down: false,
            is_vertical: false,
        }
    }
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

impl CadLinetype {
    pub fn equivalent(&self, other: &Self) -> bool {
        self == other
    }

    pub fn multiply_scale(&self, factor: f64) -> Self {
        Self {
            elements: self.elements.clone(),
            scale: self.scale * factor,
            normal: self.normal,
        }
    }

    pub fn read(_snapshot: &ShadowSnapshot, _name: &str) -> Self {
        Self::default()
    }
}
