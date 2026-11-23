#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// Drawing unit conversion factors (millimeters per unit).
pub struct DrawingUnitConversion;

impl DrawingUnitConversion {
    pub fn factor(insunits: i32) -> f64 {
        Self::meters_per_unit(insunits)
    }

    pub fn meters_per_unit(insunits: i32) -> f64 {
        match insunits {
            1 => 0.0254,
            2 => 0.3048,
            3 => 1609.344,
            4 => 0.001,
            5 => 0.01,
            6 => 1.0,
            7 => 1000.0,
            _ => 1.0,
        }
    }
}

