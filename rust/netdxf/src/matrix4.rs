use std::ops::Mul;

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
