use crate::cad_scene::CadOccurrence;
use netdxf_shadow::{Bounds2, Vec3};
use serde::{Deserialize, Serialize};

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
}

impl Default for HatchIslandOptions {
    fn default() -> Self {
        Self {
            boundary_tolerance: 0.01,
            contact_tolerance: 1e-9,
            maximum_boundaries: 16_384,
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
}

pub struct HatchIslandResolver;

impl HatchIslandResolver {
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
        HatchBoundaryTopology { boundaries }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadPatternLine {
    pub origin: Vec3,
    pub angle: f64,
    pub delta: Vec3,
    pub dashes: Vec<f64>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct HatchPatternOptions {
    pub boundary_tolerance: f64,
    pub maximum_rows: usize,
    pub maximum_segments: usize,
}

impl Default for HatchPatternOptions {
    fn default() -> Self {
        Self {
            boundary_tolerance: 0.01,
            maximum_rows: 100_000,
            maximum_segments: 500_000,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DashInterval {
    pub start: f64,
    pub end: f64,
}

pub struct DashPattern;

impl DashPattern {
    pub fn intervals(dashes: &[f64], start: f64, end: f64) -> Vec<DashInterval> {
        if dashes.is_empty() || end <= start {
            return if end > start {
                vec![DashInterval { start, end }]
            } else {
                Vec::new()
            };
        }
        let period: f64 = dashes.iter().map(|d| d.abs()).sum();
        if period <= 1e-12 {
            return vec![DashInterval { start, end }];
        }
        let mut out = Vec::new();
        let mut pos = start;
        let mut idx = 0usize;
        while pos < end && out.len() < 100_000 {
            let d = dashes[idx % dashes.len()];
            let step = d.abs().max(1e-9);
            let next = (pos + step).min(end);
            if d >= 0.0 {
                out.push(DashInterval {
                    start: pos,
                    end: next,
                });
            }
            pos = next;
            idx += 1;
        }
        out
    }
}

pub struct HatchPatternGenerator;

impl HatchPatternGenerator {
    pub fn generate_line_rows(
        bounds: Bounds2,
        pattern: &CadPatternLine,
        options: &HatchPatternOptions,
    ) -> Vec<(Vec3, Vec3)> {
        if bounds.is_empty() {
            return Vec::new();
        }
        let spacing = pattern.delta.y.abs().max(1e-6);
        let rows = ((bounds.height() / spacing).ceil() as usize).min(options.maximum_rows);
        let mut out = Vec::with_capacity(rows);
        for r in 0..=rows {
            let y = bounds.min_y + r as f64 * spacing;
            if y > bounds.max_y + options.boundary_tolerance {
                break;
            }
            out.push((Vec3::xy(bounds.min_x, y), Vec3::xy(bounds.max_x, y)));
        }
        out
    }
}

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
