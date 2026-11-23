use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Tensor-product B-spline surface (`netDxf.GTE.BSplineSurface`).
#[derive(Clone, Debug, PartialEq)]
pub struct BSplineSurface {
    pub basis_u: BasisFunction,
    pub basis_v: BasisFunction,
    /// Control points stored in row-major order: `controls[u * num_v + v]`.
    pub controls: Vec<Vector3>,
}

impl BSplineSurface {
    pub fn new(
        input_u: &BasisFunctionInput,
        input_v: &BasisFunctionInput,
        controls: Vec<Vector3>,
    ) -> Self {
        Self {
            basis_u: BasisFunction::new(input_u),
            basis_v: BasisFunction::new(input_v),
            controls,
        }
    }

    pub fn position(&self, u: f64, v: f64) -> Vector3 {
        let (su, du) = self.basis_u.evaluate_derivatives(u, 0);
        let (sv, dv) = self.basis_v.evaluate_derivatives(v, 0);
        let pu = self.basis_u.degree;
        let pv = self.basis_v.degree;
        let nv = self.basis_v.num_controls;

        let mut pt = Vector3::ZERO;
        for (i, &bu) in du[0].iter().enumerate() {
            let ui = su - pu + i;
            let mut row_pt = Vector3::ZERO;
            for (j, &bv) in dv[0].iter().enumerate() {
                let vj = sv - pv + j;
                row_pt += self.controls[ui * nv + vj] * bv;
            }
            pt += row_pt * bu;
        }
        pt
    }
}
