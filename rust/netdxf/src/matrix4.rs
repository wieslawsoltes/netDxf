use std::ops::Mul;

use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;
use crate::vector4::Vector4;

/// Represents a 4x4 double-precision matrix in row-major order.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Matrix4 {
    pub m: [[f64; 4]; 4],
}

impl Matrix4 {
    pub const ZERO: Self = Self { m: [[0.0; 4]; 4] };
    pub const IDENTITY: Self = Self {
        m: [
            [1.0, 0.0, 0.0, 0.0],
            [0.0, 1.0, 0.0, 0.0],
            [0.0, 0.0, 1.0, 0.0],
            [0.0, 0.0, 0.0, 1.0],
        ],
    };

    #[allow(clippy::too_many_arguments)]
    #[inline]
    pub const fn new(
        m11: f64, m12: f64, m13: f64, m14: f64,
        m21: f64, m22: f64, m23: f64, m24: f64,
        m31: f64, m32: f64, m33: f64, m34: f64,
        m41: f64, m42: f64, m43: f64, m44: f64,
    ) -> Self {
        Self {
            m: [
                [m11, m12, m13, m14],
                [m21, m22, m23, m24],
                [m31, m32, m33, m34],
                [m41, m42, m43, m44],
            ],
        }
    }

    #[inline]
    pub const fn from_rows(rows: [[f64; 4]; 4]) -> Self {
        Self { m: rows }
    }

    #[inline]
    pub const fn from_affine(rot_scale: Matrix3, translation: Vector3) -> Self {
        Self {
            m: [
                [rot_scale.m11, rot_scale.m12, rot_scale.m13, translation.x],
                [rot_scale.m21, rot_scale.m22, rot_scale.m23, translation.y],
                [rot_scale.m31, rot_scale.m32, rot_scale.m33, translation.z],
                [0.0, 0.0, 0.0, 1.0],
            ],
        }
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
        for r in 0..4 {
            for c in 0..4 {
                let expected = if r == c { 1.0 } else { 0.0 };
                if self.m[r][c] != expected {
                    return false;
                }
            }
        }
        true
    }

    #[inline]
    pub fn equals_with(self, b: Self, threshold: f64) -> bool {
        for r in 0..4 {
            for c in 0..4 {
                if !MathHelper::is_equal_with(self.m[r][c], b.m[r][c], threshold) {
                    return false;
                }
            }
        }
        true
    }

    pub fn determinant(self) -> f64 {
        let m = &self.m;
        let s0 = m[0][0] * m[1][1] - m[1][0] * m[0][1];
        let s1 = m[0][0] * m[1][2] - m[1][0] * m[0][2];
        let s2 = m[0][0] * m[1][3] - m[1][0] * m[0][3];
        let s3 = m[0][1] * m[1][2] - m[1][1] * m[0][2];
        let s4 = m[0][1] * m[1][3] - m[1][1] * m[0][3];
        let s5 = m[0][2] * m[1][3] - m[1][2] * m[0][3];

        let c5 = m[2][2] * m[3][3] - m[3][2] * m[2][3];
        let c4 = m[2][1] * m[3][3] - m[3][1] * m[2][3];
        let c3 = m[2][1] * m[3][2] - m[3][1] * m[2][2];
        let c2 = m[2][0] * m[3][3] - m[3][0] * m[2][3];
        let c1 = m[2][0] * m[3][2] - m[3][0] * m[2][2];
        let c0 = m[2][0] * m[3][1] - m[3][0] * m[2][1];

        s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0
    }

    pub fn inverse(self) -> Option<Self> {
        let m = &self.m;
        let s0 = m[0][0] * m[1][1] - m[1][0] * m[0][1];
        let s1 = m[0][0] * m[1][2] - m[1][0] * m[0][2];
        let s2 = m[0][0] * m[1][3] - m[1][0] * m[0][3];
        let s3 = m[0][1] * m[1][2] - m[1][1] * m[0][2];
        let s4 = m[0][1] * m[1][3] - m[1][1] * m[0][3];
        let s5 = m[0][2] * m[1][3] - m[1][2] * m[0][3];

        let c5 = m[2][2] * m[3][3] - m[3][2] * m[2][3];
        let c4 = m[2][1] * m[3][3] - m[3][1] * m[2][3];
        let c3 = m[2][1] * m[3][2] - m[3][1] * m[2][2];
        let c2 = m[2][0] * m[3][3] - m[3][0] * m[2][3];
        let c1 = m[2][0] * m[3][2] - m[3][0] * m[2][2];
        let c0 = m[2][0] * m[3][1] - m[3][0] * m[2][1];

        let det = s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
        if MathHelper::is_zero(det) {
            return None;
        }
        let inv = 1.0 / det;
        Some(Self::from_rows([
            [
                (m[1][1] * c5 - m[1][2] * c4 + m[1][3] * c3) * inv,
                (-m[0][1] * c5 + m[0][2] * c4 - m[0][3] * c3) * inv,
                (m[3][1] * s5 - m[3][2] * s4 + m[3][3] * s3) * inv,
                (-m[2][1] * s5 + m[2][2] * s4 - m[2][3] * s3) * inv,
            ],
            [
                (-m[1][0] * c5 + m[1][2] * c2 - m[1][3] * c1) * inv,
                (m[0][0] * c5 - m[0][2] * c2 + m[0][3] * c1) * inv,
                (-m[3][0] * s5 + m[3][2] * s2 - m[3][3] * s1) * inv,
                (m[2][0] * s5 - m[2][2] * s2 + m[2][3] * s1) * inv,
            ],
            [
                (m[1][0] * c4 - m[1][1] * c2 + m[1][3] * c0) * inv,
                (-m[0][0] * c4 + m[0][1] * c2 - m[0][3] * c0) * inv,
                (m[3][0] * s4 - m[3][1] * s2 + m[3][3] * s0) * inv,
                (-m[2][0] * s4 + m[2][1] * s2 - m[2][3] * s0) * inv,
            ],
            [
                (-m[1][0] * c3 + m[1][1] * c1 - m[1][2] * c0) * inv,
                (m[0][0] * c3 - m[0][1] * c1 + m[0][2] * c0) * inv,
                (-m[3][0] * s3 + m[3][1] * s1 - m[3][2] * s0) * inv,
                (m[2][0] * s3 - m[2][1] * s1 + m[2][2] * s0) * inv,
            ],
        ]))
    }

    #[inline]
    pub const fn transpose(self) -> Self {
        let mut r = [[0.0; 4]; 4];
        let mut i = 0;
        while i < 4 {
            let mut j = 0;
            while j < 4 {
                r[i][j] = self.m[j][i];
                j += 1;
            }
            i += 1;
        }
        Self { m: r }
    }
}

impl Default for Matrix4 {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl Mul<Vector4> for Matrix4 {
    type Output = Vector4;
    #[inline]
    fn mul(self, v: Vector4) -> Vector4 {
        if self.is_identity_exact() {
            return v;
        }
        Vector4 {
            x: self.m[0][0] * v.x + self.m[0][1] * v.y + self.m[0][2] * v.z + self.m[0][3] * v.w,
            y: self.m[1][0] * v.x + self.m[1][1] * v.y + self.m[1][2] * v.z + self.m[1][3] * v.w,
            z: self.m[2][0] * v.x + self.m[2][1] * v.y + self.m[2][2] * v.z + self.m[2][3] * v.w,
            w: self.m[3][0] * v.x + self.m[3][1] * v.y + self.m[3][2] * v.z + self.m[3][3] * v.w,
        }
    }
}

impl Mul for Matrix4 {
    type Output = Self;
    fn mul(self, b: Self) -> Self {
        let mut r = [[0.0; 4]; 4];
        for (i, row) in r.iter_mut().enumerate() {
            for (j, cell) in row.iter_mut().enumerate() {
                *cell = self.m[i][0] * b.m[0][j]
                    + self.m[i][1] * b.m[1][j]
                    + self.m[i][2] * b.m[2][j]
                    + self.m[i][3] * b.m[3][j];
            }
        }
        Self { m: r }
    }
}
