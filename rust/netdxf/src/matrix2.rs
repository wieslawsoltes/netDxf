use std::ops::Mul;

use crate::math_helper::MathHelper;
use crate::vector2::Vector2;

/// Represents a 2x2 double-precision matrix in row-major order.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Matrix2 {
    pub m11: f64,
    pub m12: f64,
    pub m21: f64,
    pub m22: f64,
}

impl Matrix2 {
    pub const ZERO: Self = Self { m11: 0.0, m12: 0.0, m21: 0.0, m22: 0.0 };
    pub const IDENTITY: Self = Self { m11: 1.0, m12: 0.0, m21: 0.0, m22: 1.0 };

    #[inline]
    pub const fn new(m11: f64, m12: f64, m21: f64, m22: f64) -> Self {
        Self { m11, m12, m21, m22 }
    }

    #[inline]
    pub fn rotation(angle: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Self { m11: cos, m12: -sin, m21: sin, m22: cos }
    }

    #[inline]
    pub const fn scale(sx: f64, sy: f64) -> Self {
        Self { m11: sx, m12: 0.0, m21: 0.0, m22: sy }
    }

    #[inline]
    pub fn determinant(self) -> f64 {
        self.m11 * self.m22 - self.m12 * self.m21
    }

    #[inline]
    pub const fn transpose(self) -> Self {
        Self { m11: self.m11, m12: self.m21, m21: self.m12, m22: self.m22 }
    }

    pub fn inverse(self) -> Option<Self> {
        let det = self.determinant();
        if MathHelper::is_zero(det) {
            return None;
        }
        let inv = 1.0 / det;
        Some(Self {
            m11: self.m22 * inv,
            m12: -self.m12 * inv,
            m21: -self.m21 * inv,
            m22: self.m11 * inv,
        })
    }

    #[inline]
    pub fn is_identity(self) -> bool {
        self.is_identity_with(MathHelper::DEFAULT_EPSILON)
    }

    #[inline]
    pub fn is_identity_with(self, threshold: f64) -> bool {
        self.equals_with(Self::IDENTITY, threshold)
    }

    #[inline]
    pub fn is_identity_exact(self) -> bool {
        self.m11 == 1.0 && self.m12 == 0.0 && self.m21 == 0.0 && self.m22 == 1.0
    }

    #[inline]
    pub fn equals_with(self, b: Self, threshold: f64) -> bool {
        MathHelper::is_equal_with(self.m11, b.m11, threshold)
            && MathHelper::is_equal_with(self.m12, b.m12, threshold)
            && MathHelper::is_equal_with(self.m21, b.m21, threshold)
            && MathHelper::is_equal_with(self.m22, b.m22, threshold)
    }

    #[allow(clippy::should_implement_trait)]
    #[inline]
    pub fn add(a: Self, b: Self) -> Self {
        Self::new(a.m11 + b.m11, a.m12 + b.m12, a.m21 + b.m21, a.m22 + b.m22)
    }

    #[inline]
    pub fn subtract(a: Self, b: Self) -> Self {
        Self::new(a.m11 - b.m11, a.m12 - b.m12, a.m21 - b.m21, a.m22 - b.m22)
    }

    #[inline]
    pub fn multiply(a: Self, b: Self) -> Self {
        a * b
    }
}

impl Default for Matrix2 {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl Mul<Vector2> for Matrix2 {
    type Output = Vector2;
    #[inline]
    fn mul(self, v: Vector2) -> Vector2 {
        Vector2 {
            x: self.m11 * v.x + self.m12 * v.y,
            y: self.m21 * v.x + self.m22 * v.y,
        }
    }
}

impl Mul for Matrix2 {
    type Output = Self;
    #[inline]
    fn mul(self, b: Self) -> Self {
        Self {
            m11: self.m11 * b.m11 + self.m12 * b.m21,
            m12: self.m11 * b.m12 + self.m12 * b.m22,
            m21: self.m21 * b.m11 + self.m22 * b.m21,
            m22: self.m21 * b.m12 + self.m22 * b.m22,
        }
    }
}
