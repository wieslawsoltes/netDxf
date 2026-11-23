use std::ops::{Add, Div, Index, IndexMut, Mul, Sub};

use crate::gte::g_vector::GVector;
use crate::gte::gaussian_elimination::GaussianElimination;

/// Dynamic-size row-major double-precision matrix (`netDxf.GTE.GMatrix`).
#[derive(Clone, Debug, PartialEq)]
pub struct GMatrix {
    rows: usize,
    cols: usize,
    elements: Vec<f64>,
}

impl GMatrix {
    pub fn zero(rows: usize, cols: usize) -> Self {
        Self {
            rows,
            cols,
            elements: vec![0.0; rows * cols],
        }
    }

    pub fn unit(rows: usize, cols: usize, r: usize, c: usize) -> Self {
        let mut m = Self::zero(rows, cols);
        m.make_unit(r, c);
        m
    }

    pub fn identity(size: usize) -> Self {
        Self::identity_rect(size, size)
    }

    pub fn identity_rect(rows: usize, cols: usize) -> Self {
        let mut m = Self::zero(rows, cols);
        m.make_identity();
        m
    }

    pub fn from_elements(rows: usize, cols: usize, elements: Vec<f64>) -> Self {
        assert_eq!(elements.len(), rows * cols, "Mismatched matrix element count");
        Self {
            rows,
            cols,
            elements,
        }
    }

    #[inline]
    pub fn get_size(&self) -> (usize, usize) {
        (self.rows, self.cols)
    }

    #[inline]
    pub fn num_rows(&self) -> usize {
        self.rows
    }

    #[inline]
    pub fn num_cols(&self) -> usize {
        self.cols
    }

    #[inline]
    pub fn num_elements(&self) -> usize {
        self.elements.len()
    }

    #[inline]
    pub fn elements(&self) -> &[f64] {
        &self.elements
    }

    #[inline]
    pub fn elements_mut(&mut self) -> &mut [f64] {
        &mut self.elements
    }

    pub fn set_row(&mut self, r: usize, vec: &GVector) {
        if r < self.rows && vec.len() == self.cols {
            for c in 0..self.cols {
                self[(r, c)] = vec[c];
            }
        }
    }

    pub fn set_col(&mut self, c: usize, vec: &GVector) {
        if c < self.cols && vec.len() == self.rows {
            for r in 0..self.rows {
                self[(r, c)] = vec[r];
            }
        }
    }

    pub fn get_row(&self, r: usize) -> GVector {
        if r < self.rows {
            let mut v = GVector::zero(self.cols);
            for c in 0..self.cols {
                v[c] = self[(r, c)];
            }
            v
        } else {
            GVector::zero(0)
        }
    }

    pub fn get_col(&self, c: usize) -> GVector {
        if c < self.cols {
            let mut v = GVector::zero(self.rows);
            for r in 0..self.rows {
                v[r] = self[(r, c)];
            }
            v
        } else {
            GVector::zero(0)
        }
    }

    pub fn make_zero(&mut self) {
        self.elements.fill(0.0);
    }

    pub fn make_unit(&mut self, r: usize, c: usize) {
        self.make_zero();
        if r < self.rows && c < self.cols {
            self[(r, c)] = 1.0;
        }
    }

    pub fn make_identity(&mut self) {
        self.make_zero();
        let n = self.rows.min(self.cols);
        for i in 0..n {
            self[(i, i)] = 1.0;
        }
    }

    pub fn make_diagonal(&mut self, d: &GVector) {
        self.make_zero();
        let n = self.rows.min(self.cols).min(d.len());
        for i in 0..n {
            self[(i, i)] = d[i];
        }
    }

    pub fn l1_norm(&self) -> f64 {
        self.elements.iter().map(|v| v.abs()).sum()
    }

    pub fn l2_norm(&self) -> f64 {
        self.elements.iter().map(|v| v * v).sum::<f64>().sqrt()
    }

    pub fn l_infinity_norm(&self) -> f64 {
        self.elements
            .iter()
            .fold(0.0_f64, |acc, &v| acc.max(v.abs()))
    }

    pub fn inverse(&self) -> Option<Self> {
        GaussianElimination::inverse(self)
    }

    pub fn determinant(&self) -> f64 {
        if self.rows != self.cols {
            return f64::NAN;
        }
        let n = self.rows;
        if n == 0 {
            return 1.0;
        }
        let mut mat = self.clone();
        let mut det = 1.0;
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
            if max_val == 0.0 {
                return 0.0;
            }
            if pivot_row != k {
                for c in k..n {
                    let tmp = mat[(k, c)];
                    mat[(k, c)] = mat[(pivot_row, c)];
                    mat[(pivot_row, c)] = tmp;
                }
                det = -det;
            }
            let diag = mat[(k, k)];
            det *= diag;
            for r in (k + 1)..n {
                let factor = mat[(r, k)] / diag;
                for c in (k + 1)..n {
                    mat[(r, c)] -= factor * mat[(k, c)];
                }
            }
        }
        det
    }

    pub fn transpose(&self) -> Self {
        let mut t = Self::zero(self.cols, self.rows);
        for r in 0..self.rows {
            for c in 0..self.cols {
                t[(c, r)] = self[(r, c)];
            }
        }
        t
    }

    /// `M * V`
    pub fn mul_vec(&self, v: &GVector) -> GVector {
        let mut out = GVector::zero(self.rows);
        for r in 0..self.rows {
            let mut sum = 0.0;
            for c in 0..self.cols {
                sum += self[(r, c)] * v[c];
            }
            out[r] = sum;
        }
        out
    }

    /// `V^T * M`
    pub fn vec_mul(v: &GVector, m: &Self) -> GVector {
        let mut out = GVector::zero(m.cols);
        for c in 0..m.cols {
            let mut sum = 0.0;
            for r in 0..m.rows {
                sum += v[r] * m[(r, c)];
            }
            out[c] = sum;
        }
        out
    }

    /// `A * B`
    pub fn mul_mat(&self, b: &Self) -> Self {
        Self::multiply_ab(self, b)
    }

    /// `A * B`
    pub fn multiply_ab(a: &Self, b: &Self) -> Self {
        if a.cols != b.rows {
            return Self::zero(0, 0);
        }
        let mut out = Self::zero(a.rows, b.cols);
        for r in 0..a.rows {
            for k in 0..a.cols {
                let a_rk = a[(r, k)];
                if a_rk != 0.0 {
                    for c in 0..b.cols {
                        out[(r, c)] += a_rk * b[(k, c)];
                    }
                }
            }
        }
        out
    }

    /// `A * B^T`
    pub fn multiply_abt(a: &Self, b: &Self) -> Self {
        if a.cols != b.cols {
            return Self::zero(0, 0);
        }
        let mut out = Self::zero(a.rows, b.rows);
        for r in 0..a.rows {
            for c in 0..b.rows {
                let mut sum = 0.0;
                for i in 0..a.cols {
                    sum += a[(r, i)] * b[(c, i)];
                }
                out[(r, c)] = sum;
            }
        }
        out
    }

    /// `A^T * B`
    pub fn multiply_atb(a: &Self, b: &Self) -> Self {
        if a.rows != b.rows {
            return Self::zero(0, 0);
        }
        let mut out = Self::zero(a.cols, b.cols);
        for r in 0..a.cols {
            for c in 0..b.cols {
                let mut sum = 0.0;
                for i in 0..a.rows {
                    sum += a[(i, r)] * b[(i, c)];
                }
                out[(r, c)] = sum;
            }
        }
        out
    }

    /// `A^T * B^T`
    pub fn multiply_atbt(a: &Self, b: &Self) -> Self {
        if a.rows != b.cols {
            return Self::zero(0, 0);
        }
        let mut out = Self::zero(a.cols, b.rows);
        for r in 0..a.cols {
            for c in 0..b.rows {
                let mut sum = 0.0;
                for i in 0..a.rows {
                    sum += a[(i, r)] * b[(c, i)];
                }
                out[(r, c)] = sum;
            }
        }
        out
    }

    /// `M * D` where `D` is diagonal stored as vector.
    pub fn multiply_md(m: &Self, d: &GVector) -> Self {
        if d.len() != m.cols {
            return Self::zero(0, 0);
        }
        let mut out = Self::zero(m.rows, m.cols);
        for r in 0..m.rows {
            for c in 0..m.cols {
                out[(r, c)] = m[(r, c)] * d[c];
            }
        }
        out
    }

    /// `D * M` where `D` is diagonal stored as vector.
    pub fn multiply_dm(d: &GVector, m: &Self) -> Self {
        if d.len() != m.rows {
            return Self::zero(0, 0);
        }
        let mut out = Self::zero(m.rows, m.cols);
        for r in 0..m.rows {
            for c in 0..m.cols {
                out[(r, c)] = d[r] * m[(r, c)];
            }
        }
        out
    }

    /// `U * V^T`
    pub fn outer_product(u: &GVector, v: &GVector) -> Self {
        let mut out = Self::zero(u.len(), v.len());
        for r in 0..u.len() {
            for c in 0..v.len() {
                out[(r, c)] = u[r] * v[c];
            }
        }
        out
    }
}

impl Index<(usize, usize)> for GMatrix {
    type Output = f64;
    #[inline]
    fn index(&self, (r, c): (usize, usize)) -> &f64 {
        &self.elements[r * self.cols + c]
    }
}

impl IndexMut<(usize, usize)> for GMatrix {
    #[inline]
    fn index_mut(&mut self, (r, c): (usize, usize)) -> &mut f64 {
        &mut self.elements[r * self.cols + c]
    }
}

impl Index<usize> for GMatrix {
    type Output = f64;
    #[inline]
    fn index(&self, idx: usize) -> &f64 {
        &self.elements[idx]
    }
}

impl IndexMut<usize> for GMatrix {
    #[inline]
    fn index_mut(&mut self, idx: usize) -> &mut f64 {
        &mut self.elements[idx]
    }
}

impl Add for &GMatrix {
    type Output = GMatrix;
    fn add(self, rhs: &GMatrix) -> GMatrix {
        if self.rows != rhs.rows || self.cols != rhs.cols {
            return GMatrix::zero(0, 0);
        }
        GMatrix::from_elements(
            self.rows,
            self.cols,
            self.elements
                .iter()
                .zip(&rhs.elements)
                .map(|(a, b)| a + b)
                .collect(),
        )
    }
}

impl Sub for &GMatrix {
    type Output = GMatrix;
    fn sub(self, rhs: &GMatrix) -> GMatrix {
        if self.rows != rhs.rows || self.cols != rhs.cols {
            return GMatrix::zero(0, 0);
        }
        GMatrix::from_elements(
            self.rows,
            self.cols,
            self.elements
                .iter()
                .zip(&rhs.elements)
                .map(|(a, b)| a - b)
                .collect(),
        )
    }
}

impl Mul<f64> for &GMatrix {
    type Output = GMatrix;
    fn mul(self, s: f64) -> GMatrix {
        GMatrix::from_elements(
            self.rows,
            self.cols,
            self.elements.iter().map(|a| a * s).collect(),
        )
    }
}

impl Div<f64> for &GMatrix {
    type Output = GMatrix;
    fn div(self, s: f64) -> GMatrix {
        GMatrix::from_elements(
            self.rows,
            self.cols,
            self.elements.iter().map(|a| a / s).collect(),
        )
    }
}
