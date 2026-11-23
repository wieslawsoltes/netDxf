#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// Cacheable entity projector with deterministic dependency tracking.
pub trait ICacheableEntityProjector: IEntityProjector {
    fn projector_key(&self) -> &str;
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ProjectionCacheOptions {
    pub maximum_entities: usize,
    pub maximum_occurrences: usize,
}

impl Default for ProjectionCacheOptions {
    fn default() -> Self {
        Self {
            maximum_entities: 65_536,
            maximum_occurrences: 262_144,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ProjectionCacheStatistics {
    pub projections: i64,
    pub projection_hits: i64,
    pub occurrence_hits: i64,
}

