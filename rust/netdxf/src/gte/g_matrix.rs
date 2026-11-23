use std::ops::{Index, IndexMut};

use crate::gte::g_vector::GVector;

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

    pub fn identity(size: usize) -> Self {
        let mut m = Self::zero(size, size);
        for i in 0..size {
            m[(i, i)] = 1.0;
        }
        m
    }

    #[inline]
    pub fn num_rows(&self) -> usize {
        self.rows
    }

    #[inline]
    pub fn num_cols(&self) -> usize {
        self.cols
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

    pub fn mul_mat(&self, b: &Self) -> Self {
        let mut out = Self::zero(self.rows, b.cols);
        for r in 0..self.rows {
            for k in 0..self.cols {
                let a_rk = self[(r, k)];
                if a_rk != 0.0 {
                    for c in 0..b.cols {
                        out[(r, c)] += a_rk * b[(k, c)];
                    }
                }
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
