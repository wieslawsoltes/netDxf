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
}
