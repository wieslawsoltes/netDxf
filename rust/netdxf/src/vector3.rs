use std::fmt;
use std::ops::{Add, AddAssign, Div, DivAssign, Index, IndexMut, Mul, MulAssign, Neg, Sub, SubAssign};

use crate::math_helper::MathHelper;
use crate::vector2::Vector2;

/// Represents a three-component vector of double-precision floating-point numbers.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Vector3 {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

impl Vector3 {
    pub const ZERO: Self = Self { x: 0.0, y: 0.0, z: 0.0 };
    pub const UNIT_X: Self = Self { x: 1.0, y: 0.0, z: 0.0 };
    pub const UNIT_Y: Self = Self { x: 0.0, y: 1.0, z: 0.0 };
    pub const UNIT_Z: Self = Self { x: 0.0, y: 0.0, z: 1.0 };
    pub const NAN: Self = Self { x: f64::NAN, y: f64::NAN, z: f64::NAN };

    #[inline]
    pub const fn new(x: f64, y: f64, z: f64) -> Self {
        Self { x, y, z }
    }

    #[inline]
    pub const fn from_array(array: [f64; 3]) -> Self {
        Self { x: array[0], y: array[1], z: array[2] }
    }

    #[inline]
    pub const fn to_array(self) -> [f64; 3] {
        [self.x, self.y, self.z]
    }

    #[inline]
    pub fn is_nan(u: Self) -> bool {
        u.x.is_nan() || u.y.is_nan() || u.z.is_nan()
    }

    #[inline]
    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite() && self.z.is_finite()
    }

    #[inline]
    pub fn dot_product(u: Self, v: Self) -> f64 {
        u.x * v.x + u.y * v.y + u.z * v.z
    }

    #[inline]
    pub fn cross_product(u: Self, v: Self) -> Self {
        Self {
            x: u.y * v.z - u.z * v.y,
            y: u.z * v.x - u.x * v.z,
            z: u.x * v.y - u.y * v.x,
        }
    }

    #[inline]
    pub fn distance(u: Self, v: Self) -> f64 {
        (u - v).modulus()
    }

    #[inline]
    pub fn square_distance(u: Self, v: Self) -> f64 {
        (u - v).modulus_squared()
    }

    #[inline]
    pub fn mid_point(u: Self, v: Self) -> Self {
        Self {
            x: (u.x + v.x) * 0.5,
            y: (u.y + v.y) * 0.5,
            z: (u.z + v.z) * 0.5,
        }
    }

    pub fn angle_between(u: Self, v: Self) -> f64 {
        let cos = Self::dot_product(u, v) / (u.modulus() * v.modulus());
        cos.clamp(-1.0, 1.0).acos()
    }

    #[inline]
    pub fn are_perpendicular(u: Self, v: Self, threshold: f64) -> bool {
        MathHelper::is_zero_with(Self::dot_product(u, v), threshold)
    }

    #[inline]
    pub fn are_parallel(u: Self, v: Self, threshold: f64) -> bool {
        let c = Self::cross_product(u, v);
        MathHelper::is_zero_with(c.x, threshold)
            && MathHelper::is_zero_with(c.y, threshold)
            && MathHelper::is_zero_with(c.z, threshold)
    }

    #[inline]
    pub fn round(u: Self, num_digits: i32) -> Self {
        let factor = 10_f64.powi(num_digits);
        Self {
            x: (u.x * factor).round() / factor,
            y: (u.y * factor).round() / factor,
            z: (u.z * factor).round() / factor,
        }
    }

    /// Normalizes a finite non-zero direction without overflow/underflow or epsilon dependence.
    pub fn normalize_finite_direction(u: Self) -> Option<Self> {
        if !u.is_finite() {
            return None;
        }
        let max_c = u.x.abs().max(u.y.abs()).max(u.z.abs());
        if max_c == 0.0 {
            return None;
        }
        let sx = u.x / max_c;
        let sy = u.y / max_c;
        let sz = u.z / max_c;
        let norm = (sx * sx + sy * sy + sz * sz).sqrt();
        Some(Self {
            x: sx / norm,
            y: sy / norm,
            z: sz / norm,
        })
    }

    #[inline]
    pub fn normalize(u: Self) -> Self {
        let m = u.modulus();
        if MathHelper::is_zero(m) {
            Self::NAN
        } else {
            let inv = 1.0 / m;
            Self { x: u.x * inv, y: u.y * inv, z: u.z * inv }
        }
    }

    #[inline]
    pub fn modulus(self) -> f64 {
        let max_c = self.x.abs().max(self.y.abs()).max(self.z.abs());
        if max_c == 0.0 {
            return 0.0;
        }
        if !max_c.is_finite() {
            return max_c;
        }
        let sx = self.x / max_c;
        let sy = self.y / max_c;
        let sz = self.z / max_c;
        max_c * (sx * sx + sy * sy + sz * sz).sqrt()
    }

    #[inline]
    pub fn modulus_squared(self) -> f64 {
        self.x * self.x + self.y * self.y + self.z * self.z
    }

    #[inline]
    pub fn equals_with(self, other: Self, threshold: f64) -> bool {
        MathHelper::is_equal_with(self.x, other.x, threshold)
            && MathHelper::is_equal_with(self.y, other.y, threshold)
            && MathHelper::is_equal_with(self.z, other.z, threshold)
    }

    #[inline]
    pub const fn to_vector2(self) -> Vector2 {
        Vector2 { x: self.x, y: self.y }
    }
}

impl From<(f64, f64, f64)> for Vector3 {
    #[inline]
    fn from((x, y, z): (f64, f64, f64)) -> Self {
        Self { x, y, z }
    }
}

impl From<[f64; 3]> for Vector3 {
    #[inline]
    fn from(a: [f64; 3]) -> Self {
        Self::from_array(a)
    }
}

impl From<Vector2> for Vector3 {
    #[inline]
    fn from(v: Vector2) -> Self {
        Self { x: v.x, y: v.y, z: 0.0 }
    }
}

impl Index<usize> for Vector3 {
    type Output = f64;
    fn index(&self, idx: usize) -> &f64 {
        match idx {
            0 => &self.x,
            1 => &self.y,
            2 => &self.z,
            _ => panic!("Vector3 index out of bounds: {idx}"),
        }
    }
}

impl IndexMut<usize> for Vector3 {
    fn index_mut(&mut self, idx: usize) -> &mut f64 {
        match idx {
            0 => &mut self.x,
            1 => &mut self.y,
            2 => &mut self.z,
            _ => panic!("Vector3 index out of bounds: {idx}"),
        }
    }
}

impl Add for Vector3 {
    type Output = Self;
    #[inline]
    fn add(self, rhs: Self) -> Self {
        Self { x: self.x + rhs.x, y: self.y + rhs.y, z: self.z + rhs.z }
    }
}
impl AddAssign for Vector3 {
    #[inline]
    fn add_assign(&mut self, rhs: Self) {
        self.x += rhs.x;
        self.y += rhs.y;
        self.z += rhs.z;
    }
}
impl Sub for Vector3 {
    type Output = Self;
    #[inline]
    fn sub(self, rhs: Self) -> Self {
        Self { x: self.x - rhs.x, y: self.y - rhs.y, z: self.z - rhs.z }
    }
}
impl SubAssign for Vector3 {
    #[inline]
    fn sub_assign(&mut self, rhs: Self) {
        self.x -= rhs.x;
        self.y -= rhs.y;
        self.z -= rhs.z;
    }
}
impl Neg for Vector3 {
    type Output = Self;
    #[inline]
    fn neg(self) -> Self {
        Self { x: -self.x, y: -self.y, z: -self.z }
    }
}
impl Mul<f64> for Vector3 {
    type Output = Self;
    #[inline]
    fn mul(self, rhs: f64) -> Self {
        Self { x: self.x * rhs, y: self.y * rhs, z: self.z * rhs }
    }
}
impl Mul<Vector3> for f64 {
    type Output = Vector3;
    #[inline]
    fn mul(self, rhs: Vector3) -> Vector3 {
        Vector3 { x: self * rhs.x, y: self * rhs.y, z: self * rhs.z }
    }
}
impl MulAssign<f64> for Vector3 {
    #[inline]
    fn mul_assign(&mut self, rhs: f64) {
        self.x *= rhs;
        self.y *= rhs;
        self.z *= rhs;
    }
}
impl Div<f64> for Vector3 {
    type Output = Self;
    #[inline]
    fn div(self, rhs: f64) -> Self {
        Self { x: self.x / rhs, y: self.y / rhs, z: self.z / rhs }
    }
}
impl DivAssign<f64> for Vector3 {
    #[inline]
    fn div_assign(&mut self, rhs: f64) {
        self.x /= rhs;
        self.y /= rhs;
        self.z /= rhs;
    }
}

impl fmt::Display for Vector3 {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}, {}, {}", self.x, self.y, self.z)
    }
}
