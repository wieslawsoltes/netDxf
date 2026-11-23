#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::{BTreeMap, BTreeSet, VecDeque};

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct TopologyOptions {
    pub endpoint_tolerance: f64,
    pub curve_tolerance: f64,
    pub maximum_candidate_pairs: usize,
    pub maximum_segments: usize,
    pub respect_elevation: bool,
    pub split_intersections: bool,
}

impl Default for TopologyOptions {
    fn default() -> Self {
        Self {
            endpoint_tolerance: 0.01,
            curve_tolerance: 0.05,
            maximum_candidate_pairs: 100_000,
            maximum_segments: 100_000,
            respect_elevation: false,
            split_intersections: false,
        }
    }
}

/// Builds a [`TopologyGraph`] from a [`CadScene`].
pub struct TopologyBuilder;

impl TopologyBuilder {
    pub fn build(scene: &CadScene, options: &TopologyOptions) -> TopologyGraph {
        let mut nodes: Vec<GraphNode> = Vec::new();
        let mut edges: Vec<GraphEdge> = Vec::new();
        let mut grid: BTreeMap<(i64, i64, i64), usize> = BTreeMap::new();
        let tol = options.endpoint_tolerance.max(1e-9);

        let quantize = |p: Vec3| -> (i64, i64, i64) {
            (
                (p.x / tol).round() as i64,
                (p.y / tol).round() as i64,
                (p.z / tol).round() as i64,
            )
        };

        let sample_opts = SamplingOptions {
            tolerance: options.curve_tolerance,
            ..Default::default()
        };

        for occ in &scene.occurrences {
            for contour in &occ.geometry.contours {
                for curve in &contour.curves {
                    let pts = CurveSampler::sample(curve, &sample_opts);
                    if pts.len() < 2 {
                        continue;
                    }
                    let start = pts[0];
                    let end = *pts.last().unwrap();
                    let a = *grid.entry(quantize(start)).or_insert_with(|| {
                        let id = nodes.len();
                        nodes.push(GraphNode {
                            id,
                            position: start,
                            edges: Vec::new(),
                        });
                        id
                    });
                    let b = *grid.entry(quantize(end)).or_insert_with(|| {
                        let id = nodes.len();
                        nodes.push(GraphNode {
                            id,
                            position: end,
                            edges: Vec::new(),
                        });
                        id
                    });
                    let length = pts.windows(2).map(|w| Vec3::distance(w[0], w[1])).sum();
                    let eid = edges.len();
                    edges.push(GraphEdge {
                        id: eid,
                        a,
                        b,
                        length,
                        entity: occ.entity.clone(),
                        occurrence: occ.key.clone(),
                    });
                    nodes[a].edges.push(eid);
                    if b != a {
                        nodes[b].edges.push(eid);
                    }
                }
            }
        }

        TopologyGraph {
            document_id: scene.document_id.clone(),
            revision: scene.revision,
            nodes,
            edges,
        }
    }
}

