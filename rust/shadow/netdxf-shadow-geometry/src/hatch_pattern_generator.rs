#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

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
    pub maximum_boundary_points: usize,
    pub maximum_edge_visits: usize,
}

impl Default for HatchPatternOptions {
    fn default() -> Self {
        Self {
            boundary_tolerance: 0.01,
            maximum_rows: 100_000,
            maximum_segments: 500_000,
            maximum_boundary_points: 262_144,
            maximum_edge_visits: 1_000_000,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DashInterval {
    pub start: f64,
    pub end: f64,
}

impl DashInterval {
    pub fn is_dot(&self) -> bool {
        (self.end - self.start).abs() <= 1e-9
    }
}

pub struct DashPattern;

impl DashPattern {
    pub fn clip(dashes: &[f64], start: f64, end: f64) -> Vec<DashInterval> {
        Self::intervals(dashes, start, end)
    }

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
    pub fn generate(
        bounds: Bounds2,
        pattern: &CadPatternLine,
        options: &HatchPatternOptions,
    ) -> Vec<(Vec3, Vec3)> {
        Self::generate_line_rows(bounds, pattern, options)
    }

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

