use bitflags::bitflags;

use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

bitflags! {
    /// Spline flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct SplineTypeFlags: i16 {
        const NONE = 0;
        const CLOSED = 1;
        const PERIODIC = 2;
        const RATIONAL = 4;
        const PLANAR = 8;
        const LINEAR = 16;
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum SplineCreationMethod {
    FitPoints,
    #[default]
    ControlPoints,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum SplineKnotParameterization {
    #[default]
    FitChord,
    FitSqrtChord,
    FitUniform,
    FitCustom,
}

/// Represents a DXF NURBS `SPLINE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Spline {
    pub common: EntityCommon,
    pub degree: i16,
    pub flags: SplineTypeFlags,
    pub knot_tolerance: f64,
    pub control_point_tolerance: f64,
    pub fit_tolerance: f64,
    pub start_tangent: Option<Vector3>,
    pub end_tangent: Option<Vector3>,
    pub knots: Vec<f64>,
    pub control_points: Vec<Vector3>,
    pub weights: Vec<f64>,
    pub fit_points: Vec<Vector3>,
    pub creation_method: SplineCreationMethod,
    pub knot_parameterization: SplineKnotParameterization,
}

impl Spline {
    pub fn from_control_points(
        control_points: Vec<Vector3>,
        degree: i16,
        is_closed_periodic: bool,
    ) -> Self {
        let deg = degree.clamp(1, 10);
        let n = control_points.len();
        let weights = vec![1.0; n];
        let knots = Self::default_clamped_knots(n, deg as usize);
        let mut flags = SplineTypeFlags::NONE;
        if is_closed_periodic {
            flags |= SplineTypeFlags::CLOSED | SplineTypeFlags::PERIODIC;
        }
        Self {
            common: EntityCommon::new(EntityType::Spline, DxfObjectCode::SPLINE),
            degree: deg,
            flags,
            knot_tolerance: 1e-7,
            control_point_tolerance: 1e-7,
            fit_tolerance: 1e-10,
            start_tangent: None,
            end_tangent: None,
            knots,
            control_points,
            weights,
            fit_points: Vec::new(),
            creation_method: SplineCreationMethod::ControlPoints,
            knot_parameterization: SplineKnotParameterization::FitChord,
        }
    }

    pub fn from_fit_points(fit_points: Vec<Vector3>, degree: i16) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Spline, DxfObjectCode::SPLINE),
            degree: degree.clamp(1, 10),
            flags: SplineTypeFlags::NONE,
            knot_tolerance: 1e-7,
            control_point_tolerance: 1e-7,
            fit_tolerance: 1e-10,
            start_tangent: None,
            end_tangent: None,
            knots: Vec::new(),
            control_points: Vec::new(),
            weights: Vec::new(),
            fit_points,
            creation_method: SplineCreationMethod::FitPoints,
            knot_parameterization: SplineKnotParameterization::FitChord,
        }
    }

    pub fn default_clamped_knots(num_control_points: usize, degree: usize) -> Vec<f64> {
        if num_control_points <= degree {
            return Vec::new();
        }
        let num_knots = num_control_points + degree + 1;
        let interior = num_control_points - degree;
        let mut knots = Vec::with_capacity(num_knots);
        knots.resize(degree + 1, 0.0);
        for i in 1..interior {
            knots.push(i as f64 / interior as f64);
        }
        knots.resize(knots.len() + degree + 1, 1.0);
        knots
    }

    /// Evaluates the NURBS curve at parameter `u` using Cox-de Boor recursion.
    pub fn evaluate(&self, u: f64) -> Option<Vector3> {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if n == 0 || self.knots.len() < n + p + 1 {
            return None;
        }
        let u_min = self.knots[p];
        let u_max = self.knots[n];
        let t = u.clamp(u_min, u_max);

        let span = if t >= u_max {
            n - 1
        } else {
            let mut low = p;
            let mut high = n;
            let mut mid = (low + high) / 2;
            while t < self.knots[mid] || t >= self.knots[mid + 1] {
                if t < self.knots[mid] {
                    high = mid;
                } else {
                    low = mid;
                }
                mid = (low + high) / 2;
                if low >= high {
                    break;
                }
            }
            mid
        };

        let mut basis = vec![0.0; p + 1];
        let mut left = vec![0.0; p + 1];
        let mut right = vec![0.0; p + 1];
        basis[0] = 1.0;
        for j in 1..=p {
            left[j] = t - self.knots[span + 1 - j];
            right[j] = self.knots[span + j] - t;
            let mut saved = 0.0;
            for r in 0..j {
                let denom = right[r + 1] + left[j - r];
                let temp = if denom.abs() > 0.0 {
                    basis[r] / denom
                } else {
                    0.0
                };
                basis[r] = saved + right[r + 1] * temp;
                saved = left[j - r] * temp;
            }
            basis[j] = saved;
        }

        let mut numerator = Vector3::ZERO;
        let mut denom = 0.0;
        for (j, &b_val) in basis.iter().enumerate().take(p + 1) {
            let idx = span - p + j;
            let w = self.weights.get(idx).copied().unwrap_or(1.0);
            let bw = b_val * w;
            numerator += self.control_points[idx] * bw;
            denom += bw;
        }
        if denom.abs() > 0.0 {
            Some(numerator / denom)
        } else {
            None
        }
    }

    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector3> {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if n == 0 || self.knots.len() < n + p + 1 {
            return self.fit_points.clone();
        }
        let samples = precision.max(2);
        let u_min = self.knots[p];
        let u_max = self.knots[n];
        let step = (u_max - u_min) / (samples - 1) as f64;
        let mut pts = Vec::with_capacity(samples);
        for i in 0..samples {
            let u = if i == samples - 1 { u_max } else { u_min + step * i as f64 };
            if let Some(pt) = self.evaluate(u) {
                pts.push(pt);
            }
        }
        pts
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        for pt in &mut self.control_points {
            *pt = transformation * *pt + translation;
        }
        for pt in &mut self.fit_points {
            *pt = transformation * *pt + translation;
        }
        if let Some(t) = &mut self.start_tangent {
            *t = transformation * *t;
        }
        if let Some(t) = &mut self.end_tangent {
            *t = transformation * *t;
        }
    }

    pub fn bounding_rectangle(&self) -> Option<BoundingRectangle> {
        let pts = if !self.control_points.is_empty() {
            &self.control_points
        } else {
            &self.fit_points
        };
        BoundingRectangle::from_points(pts.iter().map(|v| v.to_vector2()))
    }

    /// Evaluates the first derivative (tangent vector) at parameter `u`.
    pub fn evaluate_derivative(&self, u: f64) -> Option<Vector3> {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if n == 0 || self.knots.len() < n + p + 1 {
            return None;
        }
        let weights = if self.weights.len() == n {
            self.weights.clone()
        } else {
            vec![1.0; n]
        };
        let curve = crate::gte::NURBSCurve::from_knots(
            p,
            self.knots.clone(),
            self.control_points.clone(),
            weights,
        );
        Some(curve.evaluate(u, 1)[1])
    }

    /// Reverses the spline direction in place (control points, weights, knots, fit points, tangents).
    pub fn reverse(&mut self) {
        self.control_points.reverse();
        self.weights.reverse();
        self.fit_points.reverse();
        if let (Some(&first), Some(&last)) = (self.knots.first(), self.knots.last()) {
            let sum = first + last;
            self.knots.reverse();
            for k in &mut self.knots {
                *k = sum - *k;
            }
        }
        let old_start = self.start_tangent.map(|t| -t);
        let old_end = self.end_tangent.map(|t| -t);
        self.start_tangent = old_end;
        self.end_tangent = old_start;
    }

    /// Inserts knot `u` `times` times using Boehm's algorithm in homogeneous coordinates.
    pub fn insert_knot(&mut self, u: f64, times: usize) -> bool {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if times == 0 || n == 0 || self.knots.len() < n + p + 1 {
            return false;
        }
        let u_min = self.knots[p];
        let u_max = self.knots[n];
        if !u.is_finite() || u <= u_min || u >= u_max {
            return false;
        }
        if self.weights.len() != n {
            self.weights = vec![1.0; n];
        }

        for _ in 0..times {
            let cur_n = self.control_points.len();
            // Find knot span k such that knots[k] <= u < knots[k + 1]
            let mut k = p;
            while k + 1 < cur_n && self.knots[k + 1] <= u {
                k += 1;
            }
            let mut new_pts = Vec::with_capacity(cur_n + 1);
            let mut new_wts = Vec::with_capacity(cur_n + 1);

            for i in 0..=cur_n {
                if i <= k - p {
                    new_pts.push(self.control_points[i]);
                    new_wts.push(self.weights[i]);
                } else if i <= k {
                    let denom = self.knots[i + p] - self.knots[i];
                    let alpha = if denom.abs() > 0.0 {
                        (u - self.knots[i]) / denom
                    } else {
                        0.0
                    };
                    let w0 = self.weights[i - 1];
                    let w1 = self.weights[i];
                    let p0 = self.control_points[i - 1] * w0;
                    let p1 = self.control_points[i] * w1;
                    let w = (1.0 - alpha) * w0 + alpha * w1;
                    let pt = (p0 * (1.0 - alpha) + p1 * alpha) / w;
                    new_pts.push(pt);
                    new_wts.push(w);
                } else {
                    new_pts.push(self.control_points[i - 1]);
                    new_wts.push(self.weights[i - 1]);
                }
            }
            self.knots.insert(k + 1, u);
            self.control_points = new_pts;
            self.weights = new_wts;
        }
        true
    }

    /// Splits an open clamped NURBS spline at parameter `u` into `(left, right)` sub-splines.
    pub fn split(&self, u: f64) -> Option<(Spline, Spline)> {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if n == 0 || self.knots.len() < n + p + 1 {
            return None;
        }
        let u_min = self.knots[p];
        let u_max = self.knots[n];
        if !u.is_finite() || u <= u_min || u >= u_max {
            return None;
        }

        let mut refined = self.clone();
        let existing_mult = refined
            .knots
            .iter()
            .filter(|&&k| (k - u).abs() <= refined.knot_tolerance)
            .count();
        if existing_mult < p {
            refined.insert_knot(u, p - existing_mult);
        }

        let first_idx = refined
            .knots
            .iter()
            .position(|&k| (k - u).abs() <= refined.knot_tolerance)?;
        let last_idx = refined
            .knots
            .iter()
            .rposition(|&k| (k - u).abs() <= refined.knot_tolerance)?;

        let left_cp_count = first_idx;
        let mut left_knots = refined.knots[..=last_idx].to_vec();
        left_knots.push(u);
        let left_cps = refined.control_points[..left_cp_count].to_vec();
        let left_wts = refined.weights[..left_cp_count].to_vec();

        let mut right_knots = vec![u];
        right_knots.extend_from_slice(&refined.knots[first_idx..]);
        let right_cps = refined.control_points[(first_idx - 1)..].to_vec();
        let right_wts = refined.weights[(first_idx - 1)..].to_vec();

        let mut left = self.clone();
        left.knots = left_knots;
        left.control_points = left_cps;
        left.weights = left_wts;
        left.fit_points.clear();

        let mut right = self.clone();
        right.knots = right_knots;
        right.control_points = right_cps;
        right.weights = right_wts;
        right.fit_points.clear();

        Some((left, right))
    }

    /// Trims an open clamped NURBS spline to `[u_start, u_end]`.
    pub fn trim(&self, u_start: f64, u_end: f64) -> Option<Spline> {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if n == 0 || self.knots.len() < n + p + 1 {
            return None;
        }
        let u_min = self.knots[p];
        let u_max = self.knots[n];
        if !u_start.is_finite()
            || !u_end.is_finite()
            || u_start >= u_end
            || u_start < u_min
            || u_end > u_max
        {
            return None;
        }
        let after_start = if u_start == u_min {
            self.clone()
        } else {
            self.split(u_start)?.1
        };
        if u_end == u_max {
            Some(after_start)
        } else {
            Some(after_start.split(u_end)?.0)
        }
    }

    /// Elevates the degree of a single-segment Bezier/clamped NURBS spline by 1.
    pub fn elevate_degree(&mut self) -> bool {
        let p = self.degree as usize;
        let n = self.control_points.len();
        if n != p + 1 || self.knots.len() != 2 * (p + 1) {
            return false;
        }
        if self.weights.len() != n {
            self.weights = vec![1.0; n];
        }
        let mut new_pts = Vec::with_capacity(n + 1);
        let mut new_wts = Vec::with_capacity(n + 1);
        new_pts.push(self.control_points[0]);
        new_wts.push(self.weights[0]);
        for i in 1..=p {
            let alpha = i as f64 / (p + 1) as f64;
            let w0 = self.weights[i - 1];
            let w1 = self.weights[i];
            let w = alpha * w0 + (1.0 - alpha) * w1;
            let pt = (self.control_points[i - 1] * (alpha * w0)
                + self.control_points[i] * ((1.0 - alpha) * w1))
                / w;
            new_pts.push(pt);
            new_wts.push(w);
        }
        new_pts.push(self.control_points[p]);
        new_wts.push(self.weights[p]);

        let u0 = self.knots[0];
        let u1 = *self.knots.last().unwrap();
        let mut new_knots = vec![u0; p + 2];
        new_knots.resize(2 * (p + 2), u1);

        self.degree += 1;
        self.control_points = new_pts;
        self.weights = new_wts;
        self.knots = new_knots;
        true
    }
}
