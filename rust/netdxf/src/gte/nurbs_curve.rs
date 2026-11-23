use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Rational B-spline (NURBS) curve (`netDxf.GTE.NURBSCurve`).
#[derive(Clone, Debug, PartialEq)]
pub struct NURBSCurve {
    pub basis: BasisFunction,
    pub controls: Vec<Vector3>,
    pub weights: Vec<f64>,
}

impl NURBSCurve {
    pub fn new(input: &BasisFunctionInput, controls: Vec<Vector3>, weights: Vec<f64>) -> Self {
        Self {
            basis: BasisFunction::new(input),
            controls,
            weights,
        }
    }

    pub fn from_knots(
        degree: usize,
        knots: Vec<f64>,
        controls: Vec<Vector3>,
        weights: Vec<f64>,
    ) -> Self {
        let n = controls.len();
        Self {
            basis: BasisFunction::from_knots(n, degree, knots),
            controls,
            weights,
        }
    }

    /// Evaluates position (`[0]`) and first derivative (`[1]` when `order >= 1`) at parameter `t`.
    pub fn evaluate(&self, t: f64, order: usize) -> Vec<Vector3> {
        let (span, ders) = self.basis.evaluate_derivatives(t, order.min(2));
        let p = self.basis.degree;

        let mut x = Vector3::ZERO;
        let mut w = 0.0;
        for (j, &b) in ders[0].iter().enumerate() {
            let idx = span - p + j;
            let bw = b * self.weights[idx];
            x += self.controls[idx] * bw;
            w += bw;
        }
        let inv_w = if w.abs() > 0.0 { 1.0 / w } else { 0.0 };
        let pos = x * inv_w;

        let mut out = vec![Vector3::ZERO; order + 1];
        out[0] = pos;

        if order >= 1 && ders.len() > 1 {
            let mut xd1 = Vector3::ZERO;
            let mut wd1 = 0.0;
            for (j, &b1) in ders[1].iter().enumerate() {
                let idx = span - p + j;
                let bw1 = b1 * self.weights[idx];
                xd1 += self.controls[idx] * bw1;
                wd1 += bw1;
            }
            let d1 = (xd1 - pos * wd1) * inv_w;
            out[1] = d1;

            if order >= 2 && ders.len() > 2 {
                let mut xd2 = Vector3::ZERO;
                let mut wd2 = 0.0;
                for (j, &b2) in ders[2].iter().enumerate() {
                    let idx = span - p + j;
                    let bw2 = b2 * self.weights[idx];
                    xd2 += self.controls[idx] * bw2;
                    wd2 += bw2;
                }
                out[2] = (xd2 - d1 * (2.0 * wd1) - pos * wd2) * inv_w;
            }
        }
        out
    }

    #[inline]
    pub fn position(&self, t: f64) -> Vector3 {
        self.evaluate(t, 0)[0]
    }
}
