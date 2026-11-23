use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Non-rational B-spline curve (`netDxf.GTE.BSplineCurve`).
#[derive(Clone, Debug, PartialEq)]
pub struct BSplineCurve {
    pub basis: BasisFunction,
    pub controls: Vec<Vector3>,
}

impl BSplineCurve {
    pub fn new(input: &BasisFunctionInput, controls: Vec<Vector3>) -> Self {
        Self {
            basis: BasisFunction::new(input),
            controls,
        }
    }

    pub fn from_knots(degree: usize, knots: Vec<f64>, controls: Vec<Vector3>) -> Self {
        let n = controls.len();
        Self {
            basis: BasisFunction::from_knots(n, degree, knots),
            controls,
        }
    }

    /// Evaluates position and derivatives up to `order` at parameter `t`.
    pub fn evaluate(&self, t: f64, order: usize) -> Vec<Vector3> {
        let (span, ders) = self.basis.evaluate_derivatives(t, order);
        let p = self.basis.degree;
        let mut out = vec![Vector3::ZERO; order + 1];
        for (k, row) in ders.iter().enumerate() {
            let mut sum = Vector3::ZERO;
            for (j, &b) in row.iter().enumerate() {
                sum += self.controls[span - p + j] * b;
            }
            out[k] = sum;
        }
        out
    }

    #[inline]
    pub fn position(&self, t: f64) -> Vector3 {
        self.evaluate(t, 0)[0]
    }
}
