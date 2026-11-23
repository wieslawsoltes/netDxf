#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadFillRule, CadGradient, CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

/// Hit-testing utilities against a compiled [`RenderScene`].
pub struct RenderHitTesting;

impl RenderHitTesting {
    pub fn hit_test(scene: &RenderScene, point: Vec3, tolerance: f64) -> Vec<ObjectId> {
        let box2 = Bounds2::new(
            point.x - tolerance,
            point.y - tolerance,
            point.x + tolerance,
            point.y + tolerance,
        );
        scene
            .items
            .iter()
            .filter(|item| item.bounds.intersects(box2))
            .map(|item| item.entity.clone())
            .collect()
    }
}

