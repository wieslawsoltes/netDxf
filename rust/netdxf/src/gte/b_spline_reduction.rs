use crate::gte::{BSplineCurve, BSplineCurveFit, BasisFunctionInput};
use crate::vector3::Vector3;

/// Reduces the number of control points of an open uniform B-spline curve via L2 least-squares fitting.
#[derive(Debug, Clone)]
pub struct BSplineReduction {
    degree: usize,
    control_data: Vec<Vector3>,
}

impl BSplineReduction {
    /// Creates a reduced B-spline curve from `in_controls`, `degree`, and retention `fraction` in `[0, 1]`.
    pub fn new(in_controls: &[Vector3], degree: usize, fraction: f64) -> Self {
        let num_samples = in_controls.len();
        let target = (fraction.clamp(0.0, 1.0) * num_samples as f64).round() as usize;
        if num_samples < 2 || degree == 0 || degree >= num_samples || target >= num_samples {
            return Self {
                degree,
                control_data: in_controls.to_vec(),
            };
        }
        let num_controls = target.max(degree + 1);
        // Sample the input open uniform B-spline curve and fit with fewer control points
        let input = BasisFunctionInput::open_uniform(num_samples, degree);
        let curve = BSplineCurve::new(&input, in_controls.to_vec());
        let sample_count = (num_samples * 4).max(num_controls * 2);
        let mut samples = Vec::with_capacity(sample_count);
        for i in 0..sample_count {
            let t = i as f64 / (sample_count - 1) as f64;
            samples.push(curve.position(t));
        }
        let control_data = BSplineCurveFit::fit(degree, num_controls, &samples)
            .map(|fit| fit.curve.controls)
            .unwrap_or_else(|| in_controls.to_vec());
        Self {
            degree,
            control_data,
        }
    }

    pub fn degree(&self) -> usize {
        self.degree
    }

    pub fn control_data(&self) -> &[Vector3] {
        &self.control_data
    }
}
