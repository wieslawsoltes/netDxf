#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// Builds a [`CadScene`] from a [`ShadowSnapshot`].
pub struct CadSceneBuilder {
    projectors: Vec<Box<dyn IEntityProjector>>,
    pub cache_options: ProjectionCacheOptions,
    pub cache_stats: ProjectionCacheStatistics,
    pub build_stats: SceneBuildStatistics,
}

impl Default for CadSceneBuilder {
    fn default() -> Self {
        Self::new()
    }
}

impl CadSceneBuilder {
    pub fn new() -> Self {
        Self {
            projectors: vec![Box::new(StandardEntityProjector)],
            cache_options: ProjectionCacheOptions::default(),
            cache_stats: ProjectionCacheStatistics::default(),
            build_stats: SceneBuildStatistics::default(),
        }
    }

    pub fn add_projector(&mut self, projector: Box<dyn IEntityProjector>) {
        self.projectors.push(projector);
    }

    #[inline]
    pub fn projectors(&self) -> &[Box<dyn IEntityProjector>] {
        &self.projectors
    }

    #[inline]
    pub fn cache_statistics(&self) -> ProjectionCacheStatistics {
        self.cache_stats
    }

    #[inline]
    pub fn last_build_statistics(&self) -> SceneBuildStatistics {
        self.build_stats
    }

    #[inline]
    pub fn projection_version(&self) -> u64 {
        1
    }

    #[inline]
    pub fn is_snapshot_cacheable(snapshot: &ShadowSnapshot) -> bool {
        !snapshot.document_id.is_empty()
    }

    pub fn build(&mut self, snapshot: &ShadowSnapshot, options: &SceneOptions) -> CadScene {
        let mut occurrences = Vec::new();
        let mut scene_bounds = Bounds2::EMPTY;

        for entity_id in &snapshot.entity_ids {
            if occurrences.len() >= options.maximum_occurrences {
                break;
            }
            self.emit_entity(
                snapshot,
                entity_id,
                Affine3::IDENTITY,
                &[],
                0,
                options,
                &mut occurrences,
                &mut scene_bounds,
            );
        }

        CadScene {
            document_id: snapshot.document_id.clone(),
            revision: snapshot.revision,
            layout: options.layout.clone(),
            occurrences,
            bounds: scene_bounds,
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn emit_entity(
        &mut self,
        snapshot: &ShadowSnapshot,
        entity_id: &ObjectId,
        transform: Affine3,
        insert_path: &[ObjectId],
        depth: usize,
        options: &SceneOptions,
        occurrences: &mut Vec<CadOccurrence>,
        scene_bounds: &mut Bounds2,
    ) {
        if occurrences.len() >= options.maximum_occurrences || depth > options.maximum_depth {
            return;
        }
        let reader = snapshot.read(entity_id.clone());
        let is_visible = reader.field("IsVisible").boolean(true);
        if !is_visible && !options.include_invisible {
            return;
        }

        if reader.type_name() == "Insert" {
            let pos = reader.field("Position").vector(Vec3::ZERO);
            let scale = reader.field("Scale").vector(Vec3::new(1.0, 1.0, 1.0));
            let rot = reader.field("Rotation").number(0.0).to_radians();
            let local = Affine3::move_by(pos) * Affine3::rotate_z(rot) * Affine3::scale(scale);
            let combined = transform * local;
            let mut next_path = insert_path.to_vec();
            next_path.push(entity_id.clone());
            for child in reader.field("Block").field("Entities").items() {
                if let Some(child_id) = child.id() {
                    self.emit_entity(
                        snapshot,
                        child_id,
                        combined,
                        &next_path,
                        depth + 1,
                        options,
                        occurrences,
                        scene_bounds,
                    );
                }
            }
            return;
        }

        let layer = {
            let l = reader.field("Layer").text().to_string();
            if l.is_empty() { "0".to_string() } else { l }
        };
        let argb = reader.field("ColorArgb").number(0xFFFF_FFFFu32 as f64) as u32;

        for projector in self.projectors.iter().rev() {
            if let Some(mut geom) = projector.project(snapshot, entity_id) {
                if transform != Affine3::IDENTITY {
                    for c in &mut geom.contours {
                        for curve in &mut c.curves {
                            *curve = curve.transform(transform);
                        }
                    }
                    for pt in &mut geom.points {
                        *pt = transform.point(*pt);
                    }
                }
                let mut bounds = Bounds2::EMPTY;
                for pt in &geom.points {
                    bounds = bounds.include(*pt);
                }
                for c in &geom.contours {
                    for curve in &c.curves {
                        for p in crate::curves::CurveSampler::sample(
                            curve,
                            &crate::curves::SamplingOptions::default(),
                        ) {
                            bounds = bounds.include(p);
                        }
                    }
                }
                *scene_bounds = scene_bounds.union(bounds);
                let key = if insert_path.is_empty() {
                    entity_id.as_str().to_string()
                } else {
                    let prefix: Vec<&str> = insert_path.iter().map(|i| i.as_str()).collect();
                    format!("{}/{}", prefix.join("/"), entity_id.as_str())
                };
                occurrences.push(CadOccurrence {
                    key,
                    entity: entity_id.clone(),
                    insert_path: insert_path.to_vec(),
                    transform,
                    style: CadStyle {
                        argb,
                        layer,
                        visible: is_visible,
                        ..Default::default()
                    },
                    geometry: geom,
                    bounds,
                    components: Vec::new(),
                });
                self.cache_stats.projections += 1;
                self.build_stats.roots_projected += 1;
                break;
            }
        }
    }
}

