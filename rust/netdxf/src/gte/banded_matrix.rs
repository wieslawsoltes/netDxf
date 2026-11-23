/// Dense-to-banded linear system solver (`netDxf.GTE.BandedMatrix`).
#[derive(Clone, Debug, PartialEq)]
pub struct BandedMatrix {
    size: usize,
    lower_bands: usize,
    upper_bands: usize,
    data: Vec<f64>,
}

impl BandedMatrix {
    pub fn new(size: usize, lower_bands: usize, upper_bands: usize) -> Self {
        Self {
            size,
            lower_bands,
            upper_bands,
            data: vec![0.0; size * size],
        }
    }

    #[inline]
    pub fn size(&self) -> usize {
        self.size
    }

    #[inline]
    pub fn get(&self, r: usize, c: usize) -> f64 {
        self.data[r * self.size + c]
    }

    #[inline]
    pub fn set(&mut self, r: usize, c: usize, val: f64) {
        self.data[r * self.size + c] = val;
    }

    #[inline]
    pub fn add_assign(&mut self, r: usize, c: usize, delta: f64) {
        self.data[r * self.size + c] += delta;
    }

    pub fn lower_bands(&self) -> usize {
        self.lower_bands
    }

    pub fn upper_bands(&self) -> usize {
        self.upper_bands
    }

    /// Solves `A * X = B` for multiple right-hand sides (`B` has `size` rows and `num_cols` columns)
    /// using Gaussian elimination with partial pivoting.
    pub fn solve_system(&self, b: &[Vec<f64>]) -> Option<Vec<Vec<f64>>> {
        let n = self.size;
        if b.len() != n || n == 0 {
            return None;
        }
        let cols = b[0].len();
        let mut a = self.data.clone();
        let mut x: Vec<Vec<f64>> = b.to_vec();

        for k in 0..n {
            // Partial pivot
            let mut pivot_row = k;
            let mut pivot_val = a[k * n + k].abs();
            for r in (k + 1)..n {
                let v = a[r * n + k].abs();
                if v > pivot_val {
                    pivot_val = v;
                    pivot_row = r;
                }
            }
            if pivot_val < 1e-15 {
                return None;
            }
            if pivot_row != k {
                for c in k..n {
                    a.swap(k * n + c, pivot_row * n + c);
                }
                x.swap(k, pivot_row);
            }
            let diag = a[k * n + k];
            for r in (k + 1)..n {
                let factor = a[r * n + k] / diag;
                if factor != 0.0 {
                    a[r * n + k] = 0.0;
                    for c in (k + 1)..n {
                        a[r * n + c] -= factor * a[k * n + c];
                    }
                    for j in 0..cols {
                        x[r][j] -= factor * x[k][j];
                    }
                }
            }
        }

        // Back substitution
        for k in (0..n).rev() {
            let diag = a[k * n + k];
            for j in 0..cols {
                let mut sum = x[k][j];
                for c in (k + 1)..n {
                    sum -= a[k * n + c] * x[c][j];
                }
                x[k][j] = sum / diag;
            }
        }
        Some(x)
    }
}
