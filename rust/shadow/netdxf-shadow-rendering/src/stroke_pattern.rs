use crate::{clip_dash_pattern, RenderContour};
use netdxf_shadow::Vec3;
use serde::{Deserialize, Serialize};

/// Applies dash/gap stroke patterns to rendered polyline contours (`StrokePattern.cs`).
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct StrokePattern {
    pub dashes: Vec<f64>,
    pub phase: f64,
}

impl StrokePattern {
    /// Splits `contour` into dashed sub-contours according to `self.dashes` and `self.phase`.
    pub fn apply(&self, contour: &RenderContour) -> Vec<RenderContour> {
        if self.dashes.is_empty() || contour.points.len() < 2 {
            return vec![contour.clone()];
        }

        // Build full vertex chain (closing the loop if `contour.closed`).
        let mut pts = contour.points.clone();
        if contour.closed && pts.first() != pts.last() {
            if let Some(&first) = pts.first() {
                pts.push(first);
            }
        }

        // Compute cumulative arc lengths.
        let mut cum = Vec::with_capacity(pts.len());
        cum.push(0.0);
        for w in pts.windows(2) {
            let prev = *cum.last().unwrap_or(&0.0);
            cum.push(prev + Vec3::distance(w[0], w[1]));
        }
        let total_len = *cum.last().unwrap_or(&0.0);
        if total_len <= 1e-12 {
            return vec![contour.clone()];
        }

        let intervals = clip_dash_pattern(0.0, total_len, &self.dashes, self.phase, 100_000);
        if intervals.is_empty() {
            return Vec::new();
        }

        let mut out = Vec::with_capacity(intervals.len());
        for iv in intervals {
            let mut seg_pts = Vec::new();
            seg_pts.push(sample_at_distance(&pts, &cum, iv.start));
            for (k, &d) in cum.iter().enumerate() {
                if d > iv.start && d < iv.end {
                    seg_pts.push(pts[k]);
                }
            }
            let end_pt = sample_at_distance(&pts, &cum, iv.end);
            if seg_pts.last().copied() != Some(end_pt) || seg_pts.len() == 1 {
                seg_pts.push(end_pt);
            }
            out.push(RenderContour {
                points: seg_pts,
                closed: false,
            });
        }
        out
    }

    pub fn get_contours(&self, contours: &[RenderContour]) -> Vec<RenderContour> {
        let mut out = Vec::new();
        for c in contours {
            out.extend(self.apply(c));
        }
        out
    }
}

fn sample_at_distance(pts: &[Vec3], cum: &[f64], dist: f64) -> Vec3 {
    if pts.is_empty() {
        return Vec3::ZERO;
    }
    if dist <= 0.0 || pts.len() == 1 {
        return pts[0];
    }
    let idx = cum.partition_point(|&d| d < dist);
    if idx == 0 {
        return pts[0];
    }
    if idx >= pts.len() {
        return *pts.last().unwrap();
    }
    let d0 = cum[idx - 1];
    let d1 = cum[idx];
    let span = d1 - d0;
    if span <= 1e-12 {
        return pts[idx];
    }
    let t = ((dist - d0) / span).clamp(0.0, 1.0);
    pts[idx - 1] + (pts[idx] - pts[idx - 1]) * t
}
