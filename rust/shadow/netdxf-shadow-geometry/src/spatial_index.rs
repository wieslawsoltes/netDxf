#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// 2D bounding-box spatial index for fast hit-testing and viewport culling.
#[derive(Debug, Clone, Default)]
pub struct SpatialIndex {
    items: Vec<(Bounds2, usize)>,
}

impl SpatialIndex {
    pub fn build(occurrences: &[CadOccurrence]) -> Self {
        let items = occurrences
            .iter()
            .enumerate()
            .filter(|(_, o)| !o.bounds.is_empty())
            .map(|(i, o)| (o.bounds, i))
            .collect();
        Self { items }
    }

    pub fn query(&self, region: Bounds2) -> Vec<usize> {
        self.items
            .iter()
            .filter(|(b, _)| b.intersects(region))
            .map(|(_, i)| *i)
            .collect()
    }

    pub fn query_point(&self, point: Vec3, tolerance: f64) -> Vec<usize> {
        let region = Bounds2::new(
            point.x - tolerance,
            point.y - tolerance,
            point.x + tolerance,
            point.y + tolerance,
        );
        self.query(region)
    }
}

