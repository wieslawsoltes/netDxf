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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct RenderCompilerStatistics {
    pub items_compiled: usize,
    pub items_reused: usize,
    pub geometry_compilations: usize,
    pub item_hits: usize,
}

/// Compiles a [`CadScene`] into a flat backend-agnostic [`RenderScene`].
#[derive(Debug, Default)]
pub struct RenderCompiler {
    pub statistics: RenderCompilerStatistics,
}

impl RenderCompiler {
    pub fn new() -> Self {
        Self::default()
    }

    #[inline]
    pub fn cache_statistics(&self) -> RenderCompilerStatistics {
        self.statistics
    }

    pub fn hit_test(scene: &RenderScene, point: Vec3, tolerance: f64) -> Vec<ObjectId> {
        RenderHitTesting::hit_test(scene, point, tolerance)
    }

    pub fn compile(&mut self, scene: &CadScene, options: &RenderCompileOptions) -> RenderScene {
        let sample_opts = SamplingOptions {
            tolerance: options.curve_tolerance,
            ..Default::default()
        };
        let mut items = Vec::with_capacity(scene.occurrences.len().min(options.maximum_items));
        for occ in scene.occurrences.iter().take(options.maximum_items) {
            let mut contours = Vec::new();
            for c in &occ.geometry.contours {
                let mut pts = Vec::new();
                for curve in &c.curves {
                    let sampled = CurveSampler::sample(curve, &sample_opts);
                    if !pts.is_empty() && !sampled.is_empty() && pts.last() == sampled.first() {
                        pts.extend_from_slice(&sampled[1..]);
                    } else {
                        pts.extend(sampled);
                    }
                }
                if !pts.is_empty() {
                    contours.push(RenderContour {
                        points: pts,
                        closed: c.closed,
                    });
                }
            }
            items.push(RenderItem {
                key: occ.key.clone(),
                entity: occ.entity.clone(),
                layer: occ.style.layer.clone(),
                paint: RenderPaint {
                    argb: occ.style.argb,
                    lineweight: occ.style.lineweight,
                    pattern: (!occ.style.dashes.is_empty()).then(|| StrokePattern {
                        dashes: occ.style.dashes.clone(),
                        phase: 0.0,
                    }),
                    gradient: None,
                },
                contours,
                regions: Vec::new(),
                stroke_groups: Vec::new(),
                bounds: occ.bounds,
                clip: None,
                ..Default::default()
            });
            self.statistics.items_compiled += 1;
        }

        RenderScene {
            document_id: scene.document_id.clone(),
            revision: scene.revision,
            layout: scene.layout.clone(),
            items,
            diagnostics: Vec::new(),
            bounds: scene.bounds,
        }
    }
}

