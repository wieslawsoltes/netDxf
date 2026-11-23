#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DraftingSelectionMode {
    #[default]
    Window,
    Crossing,
    Point,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ObjectSnapMode {
    #[default]
    Endpoint,
    Midpoint,
    Center,
    Intersection,
    Perpendicular,
    Nearest,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ObjectSnap {
    pub mode: ObjectSnapMode,
    pub kind: ObjectSnapMode,
    pub point: Vec3,
    pub position: Vec3,
    pub distance: f64,
    pub approximate: bool,
    pub occurrence: String,
    pub entity: Option<ObjectId>,
}

pub struct DraftingQueries;

impl DraftingQueries {
    pub fn items(scene: &netdxf_shadow_geometry::CadScene) -> &[netdxf_shadow_geometry::CadOccurrence] {
        &scene.occurrences
    }

    pub fn root(occ: &netdxf_shadow_geometry::CadOccurrence) -> ObjectId {
        occ.insert_path
            .first()
            .cloned()
            .unwrap_or_else(|| occ.entity.clone())
    }

    pub fn expand(
        scene: &netdxf_shadow_geometry::CadScene,
        root_entity: &ObjectId,
    ) -> Vec<ObjectId> {
        scene
            .occurrences
            .iter()
            .filter(|occ| &Self::root(occ) == root_entity)
            .map(|occ| occ.entity.clone())
            .collect()
    }

    pub fn pick(
        scene: &netdxf_shadow_geometry::CadScene,
        point: Vec3,
        tolerance: f64,
    ) -> Option<ObjectId> {
        let region = netdxf_shadow::Bounds2::new(
            point.x - tolerance,
            point.y - tolerance,
            point.x + tolerance,
            point.y + tolerance,
        );
        Self::select_in_bounds(scene, region, DraftingSelectionMode::Point)
            .into_iter()
            .next()
    }

    pub fn select(
        scene: &netdxf_shadow_geometry::CadScene,
        region: netdxf_shadow::Bounds2,
        mode: DraftingSelectionMode,
    ) -> Vec<ObjectId> {
        Self::select_in_bounds(scene, region, mode)
    }

    pub fn snap(
        scene: &netdxf_shadow_geometry::CadScene,
        cursor: Vec3,
        tolerance: f64,
        mode: ObjectSnapMode,
    ) -> Option<ObjectSnap> {
        let mut best: Option<(f64, ObjectSnap)> = None;
        for occ in &scene.occurrences {
            let mut candidates: Vec<Vec3> = Vec::new();
            match mode {
                ObjectSnapMode::Center => {
                    if !occ.bounds.is_empty() {
                        candidates.push(occ.bounds.center());
                    }
                }
                _ => {
                    for c in &occ.geometry.contours {
                        for curve in &c.curves {
                            let pts = netdxf_shadow_geometry::CurveSampler::sample(
                                curve,
                                &netdxf_shadow_geometry::SamplingOptions::default(),
                            );
                            if let (Some(&first), Some(&last)) = (pts.first(), pts.last()) {
                                match mode {
                                    ObjectSnapMode::Endpoint => {
                                        candidates.push(first);
                                        candidates.push(last);
                                    }
                                    ObjectSnapMode::Midpoint => {
                                        candidates.push((first + last) * 0.5);
                                    }
                                    _ => {
                                        candidates.push(PlanarGeometry::nearest_on_segment(
                                            cursor, first, last,
                                        ));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            for pt in candidates {
                let d = Vec3::distance(Vec3::xy(cursor.x, cursor.y), Vec3::xy(pt.x, pt.y));
                if d <= tolerance && best.as_ref().map_or(true, |(bd, _)| d < *bd) {
                    best = Some((
                        d,
                        ObjectSnap {
                            mode,
                            kind: mode,
                            point: pt,
                            position: pt,
                            distance: d,
                            approximate: false,
                            occurrence: occ.key.clone(),
                            entity: Some(occ.entity.clone()),
                        },
                    ));
                }
            }
        }
        best.map(|(_, s)| s)
    }

    pub fn select_in_bounds(
        scene: &netdxf_shadow_geometry::CadScene,
        region: netdxf_shadow::Bounds2,
        mode: DraftingSelectionMode,
    ) -> Vec<ObjectId> {
        scene
            .occurrences
            .iter()
            .filter(|occ| match mode {
                DraftingSelectionMode::Window => {
                    !occ.bounds.is_empty()
                        && occ.bounds.min_x >= region.min_x
                        && occ.bounds.max_x <= region.max_x
                        && occ.bounds.min_y >= region.min_y
                        && occ.bounds.max_y <= region.max_y
                }
                DraftingSelectionMode::Crossing | DraftingSelectionMode::Point => {
                    occ.bounds.intersects(region)
                }
            })
            .map(|occ| occ.entity.clone())
            .collect()
    }
}

