#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// Source descriptor for text projection.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadTextSource {
    pub text: CadText,
    pub normal: Vec3,
    pub rotation: f64,
    pub style_name: String,
}

/// Text projection utility helpers.
pub struct CadTextProjection;

impl CadTextProjection {
    pub fn build_frame(origin: Vec3, normal: Vec3, rotation_radians: f64) -> Affine3 {
        Affine3::move_by(origin)
            * Affine3::object_coordinates(normal)
            * Affine3::rotate_z(rotation_radians)
    }
}

