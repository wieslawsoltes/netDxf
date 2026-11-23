use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Rational tensor-product NURBS surface (`netDxf.GTE.NURBSSurface`).
#[derive(Clone, Debug, PartialEq)]
pub struct NURBSSurface {
    pub basis_u: BasisFunction,
    pub basis_v: BasisFunction,
    pub controls: Vec<Vector3>,
    pub weights: Vec<f64>,
}

impl NURBSSurface {
    pub fn new(
        input_u: &BasisFunctionInput,
        input_v: &BasisFunctionInput,
        controls: Vec<Vector3>,
        weights: Vec<f64>,
    ) -> Self {
        Self {
            basis_u: BasisFunction::new(input_u),
            basis_v: BasisFunction::new(input_v),
            controls,
            weights,
        }
    }

    pub fn position(&self, u: f64, v: f64) -> Vector3 {
        let (su, du) = self.basis_u.evaluate_derivatives(u, 0);
        let (sv, dv) = self.basis_v.evaluate_derivatives(v, 0);
        let pu = self.basis_u.degree;
        let pv = self.basis_v.degree;
        let nv = self.basis_v.num_controls;

        let mut num = Vector3::ZERO;
        let mut den = 0.0;
        for (i, &bu) in du[0].iter().enumerate() {
            let ui = su - pu + i;
            for (j, &bv) in dv[0].iter().enumerate() {
                let vj = sv - pv + j;
                let idx = ui * nv + vj;
                let bw = bu * bv * self.weights[idx];
                num += self.controls[idx] * bw;
                den += bw;
            }
        }
        if den.abs() > 0.0 {
            num / den
        } else {
            Vector3::ZERO
        }
    }
}
