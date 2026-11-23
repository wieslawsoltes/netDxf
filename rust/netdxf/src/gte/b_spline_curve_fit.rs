use crate::gte::b_spline_curve::BSplineCurve;
use crate::gte::banded_matrix::BandedMatrix;
use crate::gte::basis_function::{BasisFunction, BasisFunctionInput};
use crate::vector3::Vector3;

/// Least-squares B-spline curve fitter (`netDxf.GTE.BSplineCurveFit`).
#[derive(Clone, Debug, PartialEq)]
pub struct BSplineCurveFit {
    pub sample_data: Vec<Vector3>,
    pub degree: usize,
    pub num_controls: usize,
    pub control_data: Vec<Vector3>,
    pub basis_function: BasisFunction,
    pub curve: BSplineCurve,
}

impl BSplineCurveFit {
    /// Fits an open uniform B-spline of `degree` with `num_controls` control points to `samples`.
    pub fn fit(degree: usize, num_controls: usize, samples: &[Vector3]) -> Option<Self> {
        let m = samples.len();
        if m < 2 || degree == 0 || num_controls <= degree || num_controls > m {
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

        let curve = BSplineCurve::new(&input, controls.clone());
        Some(Self {
            sample_data: samples.to_vec(),
            degree,
            num_controls,
            control_data: controls,
            basis_function: basis,
            curve,
        })
    }

    #[inline]
    pub fn num_samples(&self) -> usize {
        self.sample_data.len()
    }

    #[inline]
    pub fn sample_data(&self) -> &[Vector3] {
        &self.sample_data
    }

    #[inline]
    pub fn degree(&self) -> usize {
        self.degree
    }

    #[inline]
    pub fn num_controls(&self) -> usize {
        self.num_controls
    }

    #[inline]
    pub fn control_data(&self) -> &[Vector3] {
        &self.control_data
    }

    #[inline]
    pub fn basis_function(&self) -> &BasisFunction {
        &self.basis_function
    }

    /// Evaluates the `order`-th derivative (0 = position) at `t` in `[0, 1]`.
    pub fn evaluate(&self, t: f64, order: usize) -> Vector3 {
        let (imin, _imax, ders) = self.basis_function.evaluate(t, order);
        if order >= ders.len() {
            return Vector3::ZERO;
        }
        let mut acc = Vector3::ZERO;
        for (off, &w) in ders[order].iter().enumerate() {
            if let Some(&ctrl) = self.control_data.get(imin + off) {
                acc += ctrl * w;
            }
        }
        acc
    }

    #[inline]
    pub fn get_position(&self, t: f64) -> Vector3 {
        self.evaluate(t, 0)
    }

    #[inline]
    pub fn position(&self, t: f64) -> Vector3 {
        self.curve.position(t)
    }
}
