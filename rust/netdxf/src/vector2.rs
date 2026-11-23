use std::fmt;
use std::ops::{Add, AddAssign, Div, DivAssign, Index, IndexMut, Mul, MulAssign, Neg, Sub, SubAssign};

use crate::math_helper::MathHelper;
use crate::vector3::Vector3;

/// Represents a two-component vector of double-precision floating-point numbers.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Vector2 {
    pub x: f64,
    pub y: f64,
}

impl Vector2 {
    pub const ZERO: Self = Self { x: 0.0, y: 0.0 };
    pub const UNIT_X: Self = Self { x: 1.0, y: 0.0 };
    pub const UNIT_Y: Self = Self { x: 0.0, y: 1.0 };
    pub const NAN: Self = Self { x: f64::NAN, y: f64::NAN };

    #[inline]
    pub const fn new(x: f64, y: f64) -> Self {
        Self { x, y }
    }

    #[inline]
    pub const fn from_array(array: [f64; 2]) -> Self {
        Self { x: array[0], y: array[1] }
    }

    #[inline]
    pub const fn to_array(self) -> [f64; 2] {
        [self.x, self.y]
    }

    #[inline]
    pub fn is_nan(u: Self) -> bool {
        u.x.is_nan() || u.y.is_nan()
    }

    #[inline]
    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite()
    }

    #[inline]
    pub fn dot_product(u: Self, v: Self) -> f64 {
        u.x * v.x + u.y * v.y
    }

    #[inline]
    pub fn cross_product(u: Self, v: Self) -> f64 {
        u.x * v.y - u.y * v.x
    }

    #[inline]
    pub fn perpendicular(u: Self) -> Self {
        Self { x: -u.y, y: u.x }
    }

    #[inline]
    pub fn rotate(u: Self, angle: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Self {
            x: u.x * cos - u.y * sin,
            y: u.x * sin + u.y * cos,
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
        }
    }

    /// Returns the angle in radians of the vector in `[0, 2*PI)`.
    #[inline]
    pub fn angle(u: Self) -> f64 {
        let angle = u.y.atan2(u.x);
        if angle < 0.0 {
            MathHelper::TWO_PI + angle
        } else {
            angle
        }
    }

    /// Returns the angle in radians of the line from `u` to `v` in `[0, 2*PI)`.
    #[inline]
    pub fn angle_between_points(u: Self, v: Self) -> f64 {
        Self::angle(v - u)
    }

    /// Returns the angle in radians between two vectors in `[0, PI]`.
    pub fn angle_between(u: Self, v: Self) -> f64 {
        let cos = Self::dot_product(u, v) / (u.modulus() * v.modulus());
        cos.clamp(-1.0, 1.0).acos()
    }

    #[inline]
    pub fn polar(u: Self, distance: f64, angle: f64) -> Self {
        let (sin, cos) = angle.sin_cos();
        Self {
            x: u.x + distance * cos,
            y: u.y + distance * sin,
        }
    }

    #[inline]
    pub fn are_perpendicular(u: Self, v: Self, threshold: f64) -> bool {
        MathHelper::is_zero_with(Self::dot_product(u, v), threshold)
    }

    #[inline]
    pub fn are_parallel(u: Self, v: Self, threshold: f64) -> bool {
        MathHelper::is_zero_with(Self::cross_product(u, v), threshold)
    }

    #[inline]
    pub fn round(u: Self, num_digits: i32) -> Self {
        let factor = 10_f64.powi(num_digits);
        Self {
            x: (u.x * factor).round() / factor,
            y: (u.y * factor).round() / factor,
        }
    }

    #[inline]
    pub fn normalize(u: Self) -> Self {
        let m = u.modulus();
        if MathHelper::is_zero(m) {
            Self::NAN
        } else {
            let inv = 1.0 / m;
            Self { x: u.x * inv, y: u.y * inv }
        }
    }

    #[inline]
    pub fn modulus(self) -> f64 {
        self.x.hypot(self.y)
    }

    #[inline]
    pub fn modulus_squared(self) -> f64 {
        self.x * self.x + self.y * self.y
    }

    #[inline]
    pub fn equals_with(self, other: Self, threshold: f64) -> bool {
        MathHelper::is_equal_with(self.x, other.x, threshold)
            && MathHelper::is_equal_with(self.y, other.y, threshold)
    }

    #[inline]
    pub const fn to_vector3(self, z: f64) -> Vector3 {
        Vector3 { x: self.x, y: self.y, z }
    }

    #[inline]
    pub fn subtract(u: Self, v: Self) -> Self {
        u - v
    }

    #[inline]
    pub fn negate(u: Self) -> Self {
        -u
    }

    #[inline]
    pub fn multiply(u: Self, a: f64) -> Self {
        u * a
    }

    #[inline]
    pub fn divide(u: Self, a: f64) -> Self {
        u / a
    }

    #[inline]
    pub fn is_normalized(self) -> bool {
        MathHelper::is_one(self.modulus_squared())
    }
}

impl From<(f64, f64)> for Vector2 {
    #[inline]
    fn from((x, y): (f64, f64)) -> Self {
        Self { x, y }
    }
}

impl From<[f64; 2]> for Vector2 {
    #[inline]
    fn from(a: [f64; 2]) -> Self {
        Self::from_array(a)
    }
}

impl Index<usize> for Vector2 {
    type Output = f64;
    fn index(&self, idx: usize) -> &f64 {
        match idx {
            0 => &self.x,
            1 => &self.y,
            _ => panic!("Vector2 index out of bounds: {idx}"),
        }
    }
}

impl IndexMut<usize> for Vector2 {
    fn index_mut(&mut self, idx: usize) -> &mut f64 {
        match idx {
            0 => &mut self.x,
            1 => &mut self.y,
            _ => panic!("Vector2 index out of bounds: {idx}"),
        }
    }
}

impl Add for Vector2 {
    type Output = Self;
    #[inline]
    fn add(self, rhs: Self) -> Self {
        Self { x: self.x + rhs.x, y: self.y + rhs.y }
    }
}
impl AddAssign for Vector2 {
    #[inline]
    fn add_assign(&mut self, rhs: Self) {
        self.x += rhs.x;
        self.y += rhs.y;
    }
}
impl Sub for Vector2 {
    type Output = Self;
    #[inline]
    fn sub(self, rhs: Self) -> Self {
        Self { x: self.x - rhs.x, y: self.y - rhs.y }
    }
}
impl SubAssign for Vector2 {
    #[inline]
    fn sub_assign(&mut self, rhs: Self) {
        self.x -= rhs.x;
        self.y -= rhs.y;
    }
}
impl Neg for Vector2 {
    type Output = Self;
    #[inline]
    fn neg(self) -> Self {
        Self { x: -self.x, y: -self.y }
    }
}
impl Mul<f64> for Vector2 {
    type Output = Self;
    #[inline]
    fn mul(self, rhs: f64) -> Self {
        Self { x: self.x * rhs, y: self.y * rhs }
    }
}
impl Mul<Vector2> for f64 {
    type Output = Vector2;
    #[inline]
    fn mul(self, rhs: Vector2) -> Vector2 {
        Vector2 { x: self * rhs.x, y: self * rhs.y }
    }
}
impl MulAssign<f64> for Vector2 {
    #[inline]
    fn mul_assign(&mut self, rhs: f64) {
        self.x *= rhs;
        self.y *= rhs;
    }
}
impl Div<f64> for Vector2 {
    type Output = Self;
    #[inline]
    fn div(self, rhs: f64) -> Self {
        Self { x: self.x / rhs, y: self.y / rhs }
    }
}
impl DivAssign<f64> for Vector2 {
    #[inline]
    fn div_assign(&mut self, rhs: f64) {
        self.x /= rhs;
        self.y /= rhs;
    }
}

impl fmt::Display for Vector2 {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}, {}", self.x, self.y)
    }
}
