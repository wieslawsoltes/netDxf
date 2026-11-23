#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, IShadowGraphStore, ObjectId, ShadowEditBatch, ShadowGraphPatch, ShadowResult, ShadowSnapshot, ShadowValue, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::sync::Arc;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum SnapMode {
    #[default]
    None,
    Grid,
    Endpoint,
    Midpoint,
    Center,
    Nearest,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct SnapResult {
    pub point: Vec3,
    pub position: Vec3,
    pub occurrence: Option<String>,
    pub mode: SnapMode,
    pub distance: f64,
}

pub struct Snapping;

impl Snapping {
    pub fn find(point: Vec3, candidates: &[Vec3], tolerance: f64) -> Option<SnapResult> {
        Self::snap_to_endpoints(point, candidates, tolerance)
    }

    pub fn snap_to_grid(point: Vec3, spacing: f64) -> SnapResult {
        let s = spacing.max(1e-9);
        let snapped = Vec3::new(
            (point.x / s).round() * s,
            (point.y / s).round() * s,
            (point.z / s).round() * s,
        );
        SnapResult {
            point: snapped,
            position: snapped,
            occurrence: None,
            mode: SnapMode::Grid,
            distance: Vec3::distance(point, snapped),
        }
    }

    pub fn snap_to_endpoints(point: Vec3, candidates: &[Vec3], tolerance: f64) -> Option<SnapResult> {
        let mut best: Option<SnapResult> = None;
        for &c in candidates {
            let d = Vec3::distance(point, c);
            if d <= tolerance && best.as_ref().map_or(true, |b| d < b.distance) {
                best = Some(SnapResult {
                    point: c,
                    position: c,
                    occurrence: None,
                    mode: SnapMode::Endpoint,
                    distance: d,
                });
            }
        }
        best
    }
}

