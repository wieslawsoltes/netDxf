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

    pub fn contains(item: &RenderItem, point: Vec3, tolerance: f64) -> bool {
        let box2 = Bounds2::new(
            point.x - tolerance,
            point.y - tolerance,
            point.x + tolerance,
            point.y + tolerance,
        );
        item.bounds.intersects(box2)
    }

    pub fn contains_clip(clip: &RenderClip, point: Vec3) -> bool {
        Self::polygon_contains(&clip.polygon, point)
    }

    pub fn polygon_contains(polygon: &[Vec3], point: Vec3) -> bool {
        if polygon.len() < 3 {
            return false;
        }
        let mut inside = false;
        let mut j = polygon.len() - 1;
        for i in 0..polygon.len() {
            let pi = polygon[i];
            let pj = polygon[j];
            if ((pi.y > point.y) != (pj.y > point.y))
                && (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y + 1e-18) + pi.x)
            {
                inside = !inside;
            }
            j = i;
        }
        inside
    }
}

