use std::fmt;
use std::ops::{Add, Div, Index, IndexMut, Mul, Neg, Sub};

use crate::math_helper::MathHelper;

/// Represents a four-component vector of double-precision floating-point numbers.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Vector4 {
    pub x: f64,
    pub y: f64,
    pub z: f64,
    pub w: f64,
}

impl Vector4 {
    pub const ZERO: Self = Self { x: 0.0, y: 0.0, z: 0.0, w: 0.0 };
    pub const UNIT_X: Self = Self { x: 1.0, y: 0.0, z: 0.0, w: 0.0 };
    pub const UNIT_Y: Self = Self { x: 0.0, y: 1.0, z: 0.0, w: 0.0 };
    pub const UNIT_Z: Self = Self { x: 0.0, y: 0.0, z: 1.0, w: 0.0 };
    pub const UNIT_W: Self = Self { x: 0.0, y: 0.0, z: 0.0, w: 1.0 };
    pub const NAN: Self = Self { x: f64::NAN, y: f64::NAN, z: f64::NAN, w: f64::NAN };

    #[inline]
    pub const fn new(x: f64, y: f64, z: f64, w: f64) -> Self {
        Self { x, y, z, w }
    }

    #[inline]
    pub const fn from_array(array: [f64; 4]) -> Self {
        Self { x: array[0], y: array[1], z: array[2], w: array[3] }
    }

    #[inline]
    pub const fn to_array(self) -> [f64; 4] {
        [self.x, self.y, self.z, self.w]
    }

    #[inline]
    pub fn dot_product(u: Self, v: Self) -> f64 {
        u.x * v.x + u.y * v.y + u.z * v.z + u.w * v.w
    }

    #[inline]
    pub fn modulus(self) -> f64 {
        self.modulus_squared().sqrt()
    }

    #[inline]
    pub fn modulus_squared(self) -> f64 {
        self.x * self.x + self.y * self.y + self.z * self.z + self.w * self.w
    }

    #[inline]
    pub fn normalize(u: Self) -> Self {
        let m = u.modulus();
        if MathHelper::is_zero(m) {
            Self::NAN
        } else {
            let inv = 1.0 / m;
            Self { x: u.x * inv, y: u.y * inv, z: u.z * inv, w: u.w * inv }
        }
    }

    #[inline]
    pub fn equals_with(self, other: Self, threshold: f64) -> bool {
        MathHelper::is_equal_with(self.x, other.x, threshold)
            && MathHelper::is_equal_with(self.y, other.y, threshold)
            && MathHelper::is_equal_with(self.z, other.z, threshold)
            && MathHelper::is_equal_with(self.w, other.w, threshold)
    }
}

impl Index<usize> for Vector4 {
    type Output = f64;
    fn index(&self, idx: usize) -> &f64 {
        match idx {
            0 => &self.x,
            1 => &self.y,
            2 => &self.z,
            3 => &self.w,
            _ => panic!("Vector4 index out of bounds: {idx}"),
        }
    }
}

impl IndexMut<usize> for Vector4 {
    fn index_mut(&mut self, idx: usize) -> &mut f64 {
        match idx {
            0 => &mut self.x,
            1 => &mut self.y,
            2 => &mut self.z,
            3 => &mut self.w,
            _ => panic!("Vector4 index out of bounds: {idx}"),
        }
    }
}

impl Add for Vector4 {
    type Output = Self;
    #[inline]
    fn add(self, rhs: Self) -> Self {
        Self { x: self.x + rhs.x, y: self.y + rhs.y, z: self.z + rhs.z, w: self.w + rhs.w }
    }
}
impl Sub for Vector4 {
    type Output = Self;
    #[inline]
    fn sub(self, rhs: Self) -> Self {
        Self { x: self.x - rhs.x, y: self.y - rhs.y, z: self.z - rhs.z, w: self.w - rhs.w }
    }
}
impl Neg for Vector4 {
    type Output = Self;
    #[inline]
    fn neg(self) -> Self {
        Self { x: -self.x, y: -self.y, z: -self.z, w: -self.w }
    }
}
impl Mul<f64> for Vector4 {
    type Output = Self;
    #[inline]
    fn mul(self, rhs: f64) -> Self {
        Self { x: self.x * rhs, y: self.y * rhs, z: self.z * rhs, w: self.w * rhs }
    }
}
impl Mul<Vector4> for f64 {
    type Output = Vector4;
    #[inline]
    fn mul(self, rhs: Vector4) -> Vector4 {
        Vector4 { x: self * rhs.x, y: self * rhs.y, z: self * rhs.z, w: self * rhs.w }
    }
}
impl Div<f64> for Vector4 {
    type Output = Self;
    #[inline]
    fn div(self, rhs: f64) -> Self {
        Self { x: self.x / rhs, y: self.y / rhs, z: self.z / rhs, w: self.w / rhs }
    }
}

impl fmt::Display for Vector4 {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}, {}, {}, {}", self.x, self.y, self.z, self.w)
    }
}
