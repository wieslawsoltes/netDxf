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

    pub fn basis_function(&self, dim: usize) -> &BasisFunction {
        if dim == 0 {
            &self.basis_u
        } else {
            &self.basis_v
        }
    }

    pub fn num_controls(&self, dim: usize) -> usize {
        if dim == 0 {
            self.basis_u.num_controls
        } else {
            self.basis_v.num_controls
        }
    }

    pub fn get_control(&self, i: usize, j: usize) -> Option<Vector3> {
        let nv = self.basis_v.num_controls;
        self.controls.get(i * nv + j).copied()
    }

    pub fn set_control(&mut self, i: usize, j: usize, value: Vector3) -> bool {
        let nv = self.basis_v.num_controls;
        if let Some(slot) = self.controls.get_mut(i * nv + j) {
            *slot = value;
            true
        } else {
            false
        }
    }

    /// Evaluates position `[0]`, `dS/du` `[1]`, and `dS/dv` `[2]` at `(u, v)`.
    pub fn evaluate(&self, u: f64, v: f64, order: usize) -> Vec<Vector3> {
        let ord = order.min(1);
        let (su, du) = self.basis_u.evaluate_derivatives(u, ord);
        let (sv, dv) = self.basis_v.evaluate_derivatives(v, ord);
        let pu = self.basis_u.degree;
        let pv = self.basis_v.degree;
        let nv = self.basis_v.num_controls;

        let eval_pair = |ku: usize, kv: usize| -> Vector3 {
            let mut pt = Vector3::ZERO;
            for (i, &bu) in du[ku].iter().enumerate() {
                let ui = su - pu + i;
                let mut row_pt = Vector3::ZERO;
                for (j, &bv) in dv[kv].iter().enumerate() {
                    let vj = sv - pv + j;
                    row_pt += self.controls[ui * nv + vj] * bv;
                }
                pt += row_pt * bu;
            }
            pt
        };

        if ord == 0 {
            vec![eval_pair(0, 0)]
        } else {
            vec![eval_pair(0, 0), eval_pair(1, 0), eval_pair(0, 1)]
        }
    }
}
