use crate::gte::b_spline_curve::BSplineCurve;
use crate::gte::banded_matrix::BandedMatrix;
use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Least-squares B-spline curve fitter (`netDxf.GTE.BSplineCurveFit`).
#[derive(Clone, Debug, PartialEq)]
pub struct BSplineCurveFit {
    pub curve: BSplineCurve,
}

impl BSplineCurveFit {
    /// Fits an open uniform B-spline of `degree` with `num_controls` control points to `samples`.
    pub fn fit(degree: usize, num_controls: usize, samples: &[Vector3]) -> Option<Self> {
        let m = samples.len();
        if m < 2 || num_controls <= degree || num_controls > m {
            return None;
        }
        let input = BasisFunctionInput::open_uniform(num_controls, degree);
        let basis = BasisFunction::new(&input);

        // Build normal equations (A^T A) P = A^T Q
        let mut ata = BandedMatrix::new(num_controls, degree, degree);
        let mut atq = vec![vec![0.0; 3]; num_controls];
        let t_scale = 1.0 / (m - 1) as f64;

        for (s_idx, &sample) in samples.iter().enumerate() {
            let t = s_idx as f64 * t_scale;
            let (span, ders) = basis.evaluate_derivatives(t, 0);
            let min_i = span - degree;
            for (i_off, &bi) in ders[0].iter().enumerate() {
                let i = min_i + i_off;
                atq[i][0] += bi * sample.x;
                atq[i][1] += bi * sample.y;
                atq[i][2] += bi * sample.z;
                for (j_off, &bj) in ders[0].iter().enumerate() {
                    let j = min_i + j_off;
                    ata.add_assign(i, j, bi * bj);
                }
            }
        }

        let solved = ata.solve_system(&atq)?;
        let controls: Vec<Vector3> = solved
            .into_iter()
            .map(|row| Vector3::new(row[0], row[1], row[2]))
            .collect();

        Some(Self {
            curve: BSplineCurve::new(&input, controls),
        })
    }

    #[inline]
    pub fn position(&self, t: f64) -> Vector3 {
        self.curve.position(t)
    }
}
