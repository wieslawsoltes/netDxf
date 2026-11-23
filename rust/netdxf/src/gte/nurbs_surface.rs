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

    #[inline]
    pub fn basis_function(&self, dim: usize) -> &BasisFunction {
        if dim == 0 {
            &self.basis_u
        } else {
            &self.basis_v
        }
    }

    #[inline]
    pub fn num_controls(&self, dim: usize) -> usize {
        self.basis_function(dim).num_controls
    }

    #[inline]
    pub fn get_control(&self, i: usize, j: usize) -> Vector3 {
        let nv = self.basis_v.num_controls;
        self.controls
            .get(i * nv + j)
            .copied()
            .unwrap_or(Vector3::ZERO)
    }

    pub fn set_control(&mut self, i: usize, j: usize, value: Vector3) {
        let nv = self.basis_v.num_controls;
        if let Some(slot) = self.controls.get_mut(i * nv + j) {
            *slot = value;
        }
    }

    #[inline]
    pub fn get_weight(&self, i: usize, j: usize) -> f64 {
        let nv = self.basis_v.num_controls;
        self.weights.get(i * nv + j).copied().unwrap_or(1.0)
    }

    pub fn set_weight(&mut self, i: usize, j: usize, value: f64) {
        let nv = self.basis_v.num_controls;
        if let Some(slot) = self.weights.get_mut(i * nv + j) {
            *slot = value;
        }
    }

    /// Evaluates surface position and first partial derivatives `(P, dP/du, dP/dv)` when `order >= 1`.
    pub fn evaluate(&self, u: f64, v: f64, order: usize) -> Vec<Vector3> {
        let ord = order.min(1);
        let (su, du) = self.basis_u.evaluate_derivatives(u, ord);
        let (sv, dv) = self.basis_v.evaluate_derivatives(v, ord);
        let pu = self.basis_u.degree;
        let pv = self.basis_v.degree;
        let nv = self.basis_v.num_controls;

        let mut x = Vector3::ZERO;
        let mut w = 0.0;
        let mut xu = Vector3::ZERO;
        let mut wu = 0.0;
        let mut xv = Vector3::ZERO;
        let mut wv = 0.0;

        for (i, &bu0) in du[0].iter().enumerate() {
            let ui = su - pu + i;
            let bu1 = if ord >= 1 && du.len() > 1 { du[1][i] } else { 0.0 };
            for (j, &bv0) in dv[0].iter().enumerate() {
                let vj = sv - pv + j;
                let bv1 = if ord >= 1 && dv.len() > 1 { dv[1][j] } else { 0.0 };
                let idx = ui * nv + vj;
                let ctrl = self.controls[idx];
                let wt = self.weights[idx];
                let bw00 = bu0 * bv0 * wt;
                x += ctrl * bw00;
                w += bw00;
                if ord >= 1 {
                    let bw10 = bu1 * bv0 * wt;
                    xu += ctrl * bw10;
                    wu += bw10;
                    let bw01 = bu0 * bv1 * wt;
                    xv += ctrl * bw01;
                    wv += bw01;
                }
            }
        }
        let inv_w = if w.abs() > 0.0 { 1.0 / w } else { 0.0 };
        let pos = x * inv_w;
        if ord == 0 {
            vec![pos]
        } else {
            vec![pos, (xu - pos * wu) * inv_w, (xv - pos * wv) * inv_w]
        }
    }

    pub fn position(&self, u: f64, v: f64) -> Vector3 {
        self.evaluate(u, v, 0)[0]
    }
}
