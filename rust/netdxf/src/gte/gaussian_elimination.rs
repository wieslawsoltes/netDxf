use crate::gte::g_matrix::GMatrix;
use crate::gte::g_vector::GVector;

/// Gaussian elimination linear solver (`netDxf.GTE.GaussianElimination`).
pub struct GaussianElimination;

impl GaussianElimination {
    /// Solves `A * x = b` with partial pivoting. Returns `None` if `A` is singular.
    pub fn solve(a: &GMatrix, b: &GVector) -> Option<GVector> {
        let n = a.num_rows();
        if n == 0 || a.num_cols() != n || b.len() != n {
            return None;
        }
        let mut mat = a.clone();
        let mut rhs = b.clone();

        for k in 0..n {
            let mut pivot_row = k;
            let mut max_val = mat[(k, k)].abs();
            for r in (k + 1)..n {
                let v = mat[(r, k)].abs();
                if v > max_val {
                    max_val = v;
                    pivot_row = r;
                }
            }
            if max_val < 1e-15 {
                return None;
            }
            if pivot_row != k {
                for c in k..n {
                    let tmp = mat[(k, c)];
                    mat[(k, c)] = mat[(pivot_row, c)];
                    mat[(pivot_row, c)] = tmp;
                }
                let tmp = rhs[k];
                rhs[k] = rhs[pivot_row];
                rhs[pivot_row] = tmp;
            }
            let diag = mat[(k, k)];
            for r in (k + 1)..n {
                let factor = mat[(r, k)] / diag;
                mat[(r, k)] = 0.0;
                for c in (k + 1)..n {
                    mat[(r, c)] -= factor * mat[(k, c)];
                }
                rhs[r] -= factor * rhs[k];
            }
        }

        let mut x = GVector::zero(n);
        for k in (0..n).rev() {
            let mut sum = rhs[k];
            for c in (k + 1)..n {
                sum -= mat[(k, c)] * x[c];
            }
            x[k] = sum / mat[(k, k)];
        }
        Some(x)
    }

    /// Computes the inverse of a square matrix `A`. Returns `None` if singular.
    pub fn inverse(a: &GMatrix) -> Option<GMatrix> {
        let n = a.num_rows();
        if n == 0 || a.num_cols() != n {
            return None;
        }
        let mut inv = GMatrix::zero(n, n);
        for col in 0..n {
            let mut e = GVector::zero(n);
            e[col] = 1.0;
            let sol = Self::solve(a, &e)?;
            for row in 0..n {
                inv[(row, col)] = sol[row];
            }
        }
        Some(inv)
    }
}
