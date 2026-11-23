use std::cmp::Ordering;
use std::ops::{Add, Div, Index, IndexMut, Mul, Sub};

/// Dynamic-size double-precision vector (`netDxf.GTE.GVector`).
#[derive(Clone, Debug, PartialEq)]
pub struct GVector {
    elements: Vec<f64>,
}

impl GVector {
    pub fn zero(size: usize) -> Self {
        Self {
            elements: vec![0.0; size],
        }
    }

    pub fn unit(size: usize, d: usize) -> Self {
        let mut v = Self::zero(size);
        v.make_unit(d);
        v
    }

    pub fn from_vec(elements: Vec<f64>) -> Self {
        Self { elements }
    }

    #[inline]
    pub fn len(&self) -> usize {
        self.elements.len()
    }

    #[inline]
    pub fn size(&self) -> usize {
        self.elements.len()
    }

    #[inline]
    pub fn is_empty(&self) -> bool {
        self.elements.is_empty()
    }

    #[inline]
    pub fn vector(&self) -> &[f64] {
        &self.elements
    }

    #[inline]
    pub fn as_slice(&self) -> &[f64] {
        &self.elements
    }

    #[inline]
    pub fn as_mut_slice(&mut self) -> &mut [f64] {
        &mut self.elements
    }

    pub fn make_zero(&mut self) {
        self.elements.fill(0.0);
    }

    pub fn make_unit(&mut self, d: usize) {
        self.elements.fill(0.0);
        if let Some(slot) = self.elements.get_mut(d) {
            *slot = 1.0;
        }
    }

    /// Lifts n-tuple `self` to homogeneous (n+1)-tuple `(self, last)`.
    pub fn h_lift(&self, last: f64) -> Self {
        let mut out = Vec::with_capacity(self.elements.len() + 1);
        out.extend_from_slice(&self.elements);
        out.push(last);
        Self { elements: out }
    }

    /// Projects homogeneous n-tuple `(u, v[n-1])` to (n-1)-tuple `u`.
    pub fn h_project(&self) -> Option<Self> {
        if self.elements.len() > 1 {
            Some(Self {
                elements: self.elements[..self.elements.len() - 1].to_vec(),
            })
        } else {
            None
        }
    }

    /// Lifts n-tuple `(w0, w1)` to (n+1)-tuple `(w0, value, w1)` inserting at `inject`.
    pub fn lift(&self, inject: usize, value: f64) -> Self {
        let idx = inject.min(self.elements.len());
        let mut out = Vec::with_capacity(self.elements.len() + 1);
        out.extend_from_slice(&self.elements[..idx]);
        out.push(value);
        out.extend_from_slice(&self.elements[idx..]);
        Self { elements: out }
    }

    /// Projects n-tuple `(w0, v[reject], w1)` to (n-1)-tuple `(w0, w1)`.
    pub fn project(&self, reject: usize) -> Self {
        let n = self.elements.len();
        if n <= 1 {
            return Self::zero(0);
        }
        let mut out = Vec::with_capacity(n - 1);
        for (i, &val) in self.elements.iter().enumerate() {
            if i != reject {
                out.push(val);
            }
        }
        Self { elements: out }
    }

    pub fn dot(u: &Self, v: &Self) -> f64 {
        u.elements
            .iter()
            .zip(v.elements.iter())
            .map(|(a, b)| a * b)
            .sum()
    }

    pub fn length(&self) -> f64 {
        self.length_robust(false)
    }

    /// Computes Euclidean norm; when `robust` is true, scales by the max-abs component to
    /// prevent overflow on large values (`f64::MAX`) and underflow on subnormals (`2^-1074`).
    pub fn length_robust(&self, robust: bool) -> f64 {
        if !robust {
            return Self::dot(self, self).sqrt();
        }
        let max_abs = self
            .elements
            .iter()
            .fold(0.0_f64, |acc, &x| acc.max(x.abs()));
        if max_abs > 0.0 {
            if !max_abs.is_finite() {
                return max_abs;
            }
            let sum: f64 = self
                .elements
                .iter()
                .map(|&x| {
                    let s = x / max_abs;
                    s * s
                })
                .sum();
            max_abs * sum.sqrt()
        } else {
            0.0
        }
    }

    /// Returns `(normalized_vector, original_length)`. If the vector is empty or zero, returns
    /// a zero vector of the same size and `0.0`.
    pub fn normalize(&self, robust: bool) -> (Self, f64) {
        if !robust {
            let len = self.length();
            if len > 0.0 {
                let out = Self::from_vec(self.elements.iter().map(|&x| x / len).collect());
                return (out, len);
            }
            return (Self::zero(self.len()), 0.0);
        }
        let max_abs = self
            .elements
            .iter()
            .fold(0.0_f64, |acc, &x| acc.max(x.abs()));
        if max_abs > 0.0 {
            let scaled: Vec<f64> = self.elements.iter().map(|&x| x / max_abs).collect();
            let scaled_len = scaled.iter().map(|&s| s * s).sum::<f64>().sqrt();
            let full_len = max_abs * scaled_len;
            let unit = scaled.into_iter().map(|s| s / scaled_len).collect();
            (Self::from_vec(unit), full_len)
        } else {
            (Self::zero(self.len()), 0.0)
        }
    }

    /// Computes component-wise `(min, max)` across `vectors[..count]`.
    /// Matches upstream `GVector.ComputeExtremes`: preserves first extremal value (retaining
    /// zero-sign ties); a NaN in row 0 remains the seed, while later NaNs are ignored.
    pub fn compute_extremes(vectors: &[Self]) -> Option<(Self, Self)> {
        if vectors.is_empty() {
            return None;
        }
        let size = vectors[0].len();
        if vectors.iter().any(|v| v.len() != size) {
            return None;
        }
        let mut min_v = vectors[0].clone();
        let mut max_v = vectors[0].clone();
        for v in &vectors[1..] {
            for c in 0..size {
                if v[c] < min_v[c] {
                    min_v[c] = v[c];
                } else if v[c] > max_v[c] {
                    max_v[c] = v[c];
                }
            }
        }
        Some((min_v, max_v))
    }

    /// Gram-Schmidt orthonormalization in-place; returns the minimum pre-normalization length.
    pub fn orthonormalize(vectors: &mut [Self], robust: bool) -> f64 {
        if vectors.is_empty() {
            return 0.0;
        }
        let (v0, mut min_len) = vectors[0].normalize(robust);
        vectors[0] = v0;
        for i in 1..vectors.len() {
            for j in 0..i {
                let dot = Self::dot(&vectors[i], &vectors[j]);
                let sub = &vectors[j] * dot;
                vectors[i] = &vectors[i] - &sub;
            }
            let (vi, len) = vectors[i].normalize(robust);
            vectors[i] = vi;
            if len < min_len {
                min_len = len;
            }
        }
        min_len
    }
}

impl PartialOrd for GVector {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        if self.len() != other.len() {
            return None;
        }
        for (&a, &b) in self.elements.iter().zip(other.elements.iter()) {
            let ord = a.total_cmp(&b);
            if ord != Ordering::Equal {
                return Some(ord);
            }
        }
        Some(Ordering::Equal)
    }
}

impl Index<usize> for GVector {
    type Output = f64;
    #[inline]
    fn index(&self, idx: usize) -> &f64 {
        &self.elements[idx]
    }
}

impl IndexMut<usize> for GVector {
    #[inline]
    fn index_mut(&mut self, idx: usize) -> &mut f64 {
        &mut self.elements[idx]
    }
}

impl Add for &GVector {
    type Output = GVector;
    fn add(self, rhs: &GVector) -> GVector {
        GVector::from_vec(
            self.elements
                .iter()
                .zip(&rhs.elements)
                .map(|(a, b)| a + b)
                .collect(),
        )
    }
}

impl Sub for &GVector {
    type Output = GVector;
    fn sub(self, rhs: &GVector) -> GVector {
        GVector::from_vec(
            self.elements
                .iter()
                .zip(&rhs.elements)
                .map(|(a, b)| a - b)
                .collect(),
        )
    }
}

impl Mul<f64> for &GVector {
    type Output = GVector;
    fn mul(self, s: f64) -> GVector {
        GVector::from_vec(self.elements.iter().map(|a| a * s).collect())
    }
}

impl Div<f64> for &GVector {
    type Output = GVector;
    fn div(self, s: f64) -> GVector {
        GVector::from_vec(self.elements.iter().map(|a| a / s).collect())
    }
}
