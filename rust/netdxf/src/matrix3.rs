use std::ops::{Add, Mul, Sub};

use crate::math_helper::MathHelper;
use crate::vector3::Vector3;

/// Represents a 3x3 double-precision matrix in row-major order.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Matrix3 {
    pub m11: f64,
    pub m12: f64,
    pub m13: f64,
    pub m21: f64,
    pub m22: f64,
    pub m23: f64,
    pub m31: f64,
    pub m32: f64,
    pub m33: f64,
}

impl Matrix3 {
    pub const ZERO: Self = Self {
        m11: 0.0, m12: 0.0, m13: 0.0,
        m21: 0.0, m22: 0.0, m23: 0.0,
        m31: 0.0, m32: 0.0, m33: 0.0,
    };

    pub const IDENTITY: Self = Self {
        m11: 1.0, m12: 0.0, m13: 0.0,
        m21: 0.0, m22: 1.0, m23: 0.0,
        m31: 0.0, m32: 0.0, m33: 1.0,
    };

    #[allow(clippy::too_many_arguments)]
    #[inline]
    pub const fn new(
        m11: f64, m12: f64, m13: f64,
        m21: f64, m22: f64, m23: f64,
        m31: f64, m32: f64, m33: f64,
    ) -> Self {
        Self { m11, m12, m13, m21, m22, m23, m31, m32, m33 }
    }

    #[inline]
    pub fn rotation_x(angle: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Self::new(1.0, 0.0, 0.0, 0.0, cos, -sin, 0.0, sin, cos)
    }

    #[inline]
    pub fn rotation_y(angle: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Self::new(cos, 0.0, sin, 0.0, 1.0, 0.0, -sin, 0.0, cos)
    }

    #[inline]
    pub fn rotation_z(angle: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Self::new(cos, -sin, 0.0, sin, cos, 0.0, 0.0, 0.0, 1.0)
    }

    #[inline]
    pub const fn scale(sx: f64, sy: f64, sz: f64) -> Self {
        Self::new(sx, 0.0, 0.0, 0.0, sy, 0.0, 0.0, 0.0, sz)
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
        self.m11 == 1.0
            && self.m12 == 0.0
            && self.m13 == 0.0
            && self.m21 == 0.0
            && self.m22 == 1.0
            && self.m23 == 0.0
            && self.m31 == 0.0
            && self.m32 == 0.0
            && self.m33 == 1.0
    }

    #[inline]
    pub fn equals_with(self, b: Self, threshold: f64) -> bool {
        MathHelper::is_equal_with(self.m11, b.m11, threshold)
            && MathHelper::is_equal_with(self.m12, b.m12, threshold)
            && MathHelper::is_equal_with(self.m13, b.m13, threshold)
            && MathHelper::is_equal_with(self.m21, b.m21, threshold)
            && MathHelper::is_equal_with(self.m22, b.m22, threshold)
            && MathHelper::is_equal_with(self.m23, b.m23, threshold)
            && MathHelper::is_equal_with(self.m31, b.m31, threshold)
            && MathHelper::is_equal_with(self.m32, b.m32, threshold)
            && MathHelper::is_equal_with(self.m33, b.m33, threshold)
    }

    #[inline]
    pub fn determinant(self) -> f64 {
        self.m11 * (self.m22 * self.m33 - self.m23 * self.m32)
            - self.m12 * (self.m21 * self.m33 - self.m23 * self.m31)
            + self.m13 * (self.m21 * self.m32 - self.m22 * self.m31)
    }

    #[inline]
    pub const fn transpose(self) -> Self {
        Self {
            m11: self.m11, m12: self.m21, m13: self.m31,
            m21: self.m12, m22: self.m22, m23: self.m32,
            m31: self.m13, m32: self.m23, m33: self.m33,
        }
    }

    pub fn inverse(self) -> Option<Self> {
        let det = self.determinant();
        if MathHelper::is_zero(det) {
            return None;
        }
        let inv = 1.0 / det;
        Some(Self {
            m11: (self.m22 * self.m33 - self.m23 * self.m32) * inv,
            m12: (self.m13 * self.m32 - self.m12 * self.m33) * inv,
            m13: (self.m12 * self.m23 - self.m13 * self.m22) * inv,
            m21: (self.m23 * self.m31 - self.m21 * self.m33) * inv,
            m22: (self.m11 * self.m33 - self.m13 * self.m31) * inv,
            m23: (self.m13 * self.m21 - self.m11 * self.m23) * inv,
            m31: (self.m21 * self.m32 - self.m22 * self.m31) * inv,
            m32: (self.m12 * self.m31 - self.m11 * self.m32) * inv,
            m33: (self.m11 * self.m22 - self.m12 * self.m21) * inv,
        })
    }
}

impl Default for Matrix3 {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl Add for Matrix3 {
    type Output = Self;
    #[inline]
    fn add(self, b: Self) -> Self {
        Self::new(
            self.m11 + b.m11, self.m12 + b.m12, self.m13 + b.m13,
            self.m21 + b.m21, self.m22 + b.m22, self.m23 + b.m23,
            self.m31 + b.m31, self.m32 + b.m32, self.m33 + b.m33,
        )
    }
}

impl Sub for Matrix3 {
    type Output = Self;
    #[inline]
    fn sub(self, b: Self) -> Self {
        Self::new(
            self.m11 - b.m11, self.m12 - b.m12, self.m13 - b.m13,
            self.m21 - b.m21, self.m22 - b.m22, self.m23 - b.m23,
            self.m31 - b.m31, self.m32 - b.m32, self.m33 - b.m33,
        )
    }
}

impl Mul<f64> for Matrix3 {
    type Output = Self;
    #[inline]
    fn mul(self, s: f64) -> Self {
        Self::new(
            self.m11 * s, self.m12 * s, self.m13 * s,
            self.m21 * s, self.m22 * s, self.m23 * s,
            self.m31 * s, self.m32 * s, self.m33 * s,
        )
    }
}

impl Mul<Vector3> for Matrix3 {
    type Output = Vector3;
    #[inline]
    fn mul(self, v: Vector3) -> Vector3 {
        if self.is_identity_exact() {
            return v;
        }
        Vector3 {
            x: self.m11 * v.x + self.m12 * v.y + self.m13 * v.z,
            y: self.m21 * v.x + self.m22 * v.y + self.m23 * v.z,
            z: self.m31 * v.x + self.m32 * v.y + self.m33 * v.z,
        }
    }
}

impl Mul for Matrix3 {
    type Output = Self;
    #[inline]
    fn mul(self, b: Self) -> Self {
        Self {
            m11: self.m11 * b.m11 + self.m12 * b.m21 + self.m13 * b.m31,
            m12: self.m11 * b.m12 + self.m12 * b.m22 + self.m13 * b.m32,
            m13: self.m11 * b.m13 + self.m12 * b.m23 + self.m13 * b.m33,
            m21: self.m21 * b.m11 + self.m22 * b.m21 + self.m23 * b.m31,
            m22: self.m21 * b.m12 + self.m22 * b.m22 + self.m23 * b.m32,
            m23: self.m21 * b.m13 + self.m22 * b.m23 + self.m23 * b.m33,
            m31: self.m31 * b.m11 + self.m32 * b.m21 + self.m33 * b.m31,
            m32: self.m31 * b.m12 + self.m32 * b.m22 + self.m33 * b.m32,
            m33: self.m31 * b.m13 + self.m32 * b.m23 + self.m33 * b.m33,
        }
    }
}
