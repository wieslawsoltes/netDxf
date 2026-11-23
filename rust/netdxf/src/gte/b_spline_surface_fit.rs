use crate::gte::b_spline_surface::BSplineSurface;
use crate::gte::banded_matrix::BandedMatrix;
use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Least-squares B-spline surface fitter (`netDxf.GTE.BSplineSurfaceFit`).
#[derive(Clone, Debug, PartialEq)]
pub struct BSplineSurfaceFit {
    pub surface: BSplineSurface,
}

impl BSplineSurfaceFit {
    /// Fits a tensor-product B-spline surface to a grid of `num_samples_u x num_samples_v` points.
    pub fn fit(
        degree_u: usize,
        num_controls_u: usize,
        degree_v: usize,
        num_controls_v: usize,
        num_samples_u: usize,
        num_samples_v: usize,
        samples: &[Vector3],
    ) -> Option<Self> {
        if samples.len() != num_samples_u * num_samples_v
            || num_samples_u < 2
            || num_samples_v < 2
            || num_controls_u <= degree_u
            || num_controls_v <= degree_v
            || num_controls_u > num_samples_u
            || num_controls_v > num_samples_v
        {
            return None;
        }

        let input_u = BasisFunctionInput::open_uniform(num_controls_u, degree_u);
        let input_v = BasisFunctionInput::open_uniform(num_controls_v, degree_v);
        let basis_u = BasisFunction::new(&input_u);
        let basis_v = BasisFunction::new(&input_v);

        // Fit along U first for each sample column v
        let mut ata_u = BandedMatrix::new(num_controls_u, degree_u, degree_u);
        let u_scale = 1.0 / (num_samples_u - 1) as f64;
        for su in 0..num_samples_u {
            let (span, ders) = basis_u.evaluate_derivatives(su as f64 * u_scale, 0);
            let min_i = span - degree_u;
            for (i_off, &bi) in ders[0].iter().enumerate() {
                for (j_off, &bj) in ders[0].iter().enumerate() {
                    ata_u.add_assign(min_i + i_off, min_i + j_off, bi * bj);
                }
            }
        }

        let mut rhs_u = vec![vec![0.0; num_samples_v * 3]; num_controls_u];
        for su in 0..num_samples_u {
            let (span, ders) = basis_u.evaluate_derivatives(su as f64 * u_scale, 0);
            let min_i = span - degree_u;
            for (i_off, &bi) in ders[0].iter().enumerate() {
                let i = min_i + i_off;
                for sv in 0..num_samples_v {
                    let p = samples[su * num_samples_v + sv];
                    rhs_u[i][sv * 3] += bi * p.x;
                    rhs_u[i][sv * 3 + 1] += bi * p.y;
                    rhs_u[i][sv * 3 + 2] += bi * p.z;
                }
            }
        }
        let inter = ata_u.solve_system(&rhs_u)?;

        // Fit along V for each control row u
        let mut ata_v = BandedMatrix::new(num_controls_v, degree_v, degree_v);
        let v_scale = 1.0 / (num_samples_v - 1) as f64;
        for sv in 0..num_samples_v {
            let (span, ders) = basis_v.evaluate_derivatives(sv as f64 * v_scale, 0);
            let min_i = span - degree_v;
            for (i_off, &bi) in ders[0].iter().enumerate() {
                for (j_off, &bj) in ders[0].iter().enumerate() {
                    ata_v.add_assign(min_i + i_off, min_i + j_off, bi * bj);
                }
            }
        }

        let mut rhs_v = vec![vec![0.0; num_controls_u * 3]; num_controls_v];
        for sv in 0..num_samples_v {
            let (span, ders) = basis_v.evaluate_derivatives(sv as f64 * v_scale, 0);
            let min_i = span - degree_v;
            for (i_off, &bi) in ders[0].iter().enumerate() {
                let j = min_i + i_off;
                for (cu, inter_row) in inter.iter().enumerate().take(num_controls_u) {
                    rhs_v[j][cu * 3] += bi * inter_row[sv * 3];
                    rhs_v[j][cu * 3 + 1] += bi * inter_row[sv * 3 + 1];
                    rhs_v[j][cu * 3 + 2] += bi * inter_row[sv * 3 + 2];
                }
            }
        }
        let solved = ata_v.solve_system(&rhs_v)?;

        let mut controls = vec![Vector3::ZERO; num_controls_u * num_controls_v];
        for cu in 0..num_controls_u {
            for cv in 0..num_controls_v {
                controls[cu * num_controls_v + cv] = Vector3::new(
                    solved[cv][cu * 3],
                    solved[cv][cu * 3 + 1],
                    solved[cv][cu * 3 + 2],
                );
            }
        }

        Some(Self {
            surface: BSplineSurface::new(&input_u, &input_v, controls),
        })
    }

    #[inline]
    pub fn position(&self, u: f64, v: f64) -> Vector3 {
        self.surface.position(u, v)
    }
}
