#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadHatchIslandStyle {
    #[default]
    Normal,
    Outer,
    Ignore,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct HatchIslandOptions {
    pub boundary_tolerance: f64,
    pub contact_tolerance: f64,
    pub maximum_boundaries: usize,
    pub maximum_contours: usize,
    pub maximum_boundary_points: usize,
    pub maximum_segment_comparisons: usize,
    pub maximum_containment_edge_visits: usize,
}

impl Default for HatchIslandOptions {
    fn default() -> Self {
        Self {
            boundary_tolerance: 0.01,
            contact_tolerance: 1e-9,
            maximum_boundaries: 16_384,
            maximum_contours: 16_384,
            maximum_boundary_points: 262_144,
            maximum_segment_comparisons: 1_000_000,
            maximum_containment_edge_visits: 1_000_000,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct HatchBoundaryInfo {
    pub source_index: usize,
    pub parent_index: isize,
    pub depth: usize,
    pub bounds: Bounds2,
}

#[derive(Debug, Clone, Default)]
pub struct HatchBoundaryTopology {
    pub boundaries: Vec<HatchBoundaryInfo>,
    pub loops: Vec<HatchBoundaryInfo>,
    pub sampled_point_count: usize,
    pub segment_comparisons: usize,
    pub containment_edge_visits: usize,
}

impl HatchBoundaryTopology {
    pub fn select(&self, style: CadHatchIslandStyle) -> Vec<HatchBoundaryInfo> {
        self.boundaries
            .iter()
            .copied()
            .filter(|b| match style {
                CadHatchIslandStyle::Normal => true,
                CadHatchIslandStyle::Outer => b.depth <= 1,
                CadHatchIslandStyle::Ignore => b.depth == 0,
            })
            .collect()
    }
}

pub struct HatchIslandResolver;

impl HatchIslandResolver {
    pub fn analyze(bounds_list: &[Bounds2], options: &HatchIslandOptions) -> HatchBoundaryTopology {
        Self::resolve(bounds_list, options)
    }

    pub fn select(
        topology: &HatchBoundaryTopology,
        style: CadHatchIslandStyle,
    ) -> Vec<HatchBoundaryInfo> {
        topology.select(style)
    }

    pub fn resolve(bounds_list: &[Bounds2], _options: &HatchIslandOptions) -> HatchBoundaryTopology {
        let mut boundaries = Vec::with_capacity(bounds_list.len());
        for (i, &b) in bounds_list.iter().enumerate() {
            let mut parent: isize = -1;
            let mut depth = 0usize;
            for (j, &other) in bounds_list.iter().enumerate() {
                if i != j
                    && !other.is_empty()
                    && !b.is_empty()
                    && other.min_x <= b.min_x
                    && other.min_y <= b.min_y
                    && other.max_x >= b.max_x
                    && other.max_y >= b.max_y
                {
                    depth += 1;
                    parent = j as isize;
                }
            }
            boundaries.push(HatchBoundaryInfo {
                source_index: i,
                parent_index: parent,
                depth,
                bounds: b,
            });
        }
        HatchBoundaryTopology {
            loops: boundaries.clone(),
            boundaries,
            ..Default::default()
        }
    }
}

