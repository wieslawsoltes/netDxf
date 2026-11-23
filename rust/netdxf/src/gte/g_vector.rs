use std::ops::{Add, Index, IndexMut, Mul, Sub};

/// Dynamic-size double-precision vector (`netDxf.GTE.GVector`).
#[derive(Clone, Debug, PartialEq)]
pub struct GVector {
    elements: Vec<f64>,
}

impl GVector {
    pub fn zero(size: usize) -> Self {
        Self { elements: vec![0.0; size] }
    }

    pub fn from_vec(elements: Vec<f64>) -> Self {
        Self { elements }
    }

    #[inline]
    pub fn len(&self) -> usize {
        self.elements.len()
    }

    #[inline]
    pub fn is_empty(&self) -> bool {
        self.elements.is_empty()
    }

    #[inline]
    pub fn as_slice(&self) -> &[f64] {
        &self.elements
    }

    #[inline]
    pub fn as_mut_slice(&mut self) -> &mut [f64] {
        &mut self.elements
    }

    pub fn dot(u: &Self, v: &Self) -> f64 {
        u.elements.iter().zip(v.elements.iter()).map(|(a, b)| a * b).sum()
    }

    pub fn length(&self) -> f64 {
        Self::dot(self, self).sqrt()
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
        GVector::from_vec(self.elements.iter().zip(&rhs.elements).map(|(a, b)| a + b).collect())
    }
}

impl Sub for &GVector {
    type Output = GVector;
    fn sub(self, rhs: &GVector) -> GVector {
        GVector::from_vec(self.elements.iter().zip(&rhs.elements).map(|(a, b)| a - b).collect())
    }
}

impl Mul<f64> for &GVector {
    type Output = GVector;
    fn mul(self, s: f64) -> GVector {
        GVector::from_vec(self.elements.iter().map(|a| a * s).collect())
    }
}
