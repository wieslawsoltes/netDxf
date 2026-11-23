use serde::{Deserialize, Serialize};
use std::fmt;
use std::ops::{Add, Div, Mul, Neg, Sub};

/// Double-precision interchange math, independent of a geometry or rendering engine.
#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct Vec3 {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

impl Vec3 {
    pub const ZERO: Self = Self {
        x: 0.0,
        y: 0.0,
        z: 0.0,
    };
    pub const UNIT_X: Self = Self {
        x: 1.0,
        y: 0.0,
        z: 0.0,
    };
    pub const UNIT_Y: Self = Self {
        x: 0.0,
        y: 1.0,
        z: 0.0,
    };
    pub const UNIT_Z: Self = Self {
        x: 0.0,
        y: 0.0,
        z: 1.0,
    };

    #[inline]
    pub const fn new(x: f64, y: f64, z: f64) -> Self {
        Self { x, y, z }
    }

    #[inline]
    pub const fn xy(x: f64, y: f64) -> Self {
        Self { x, y, z: 0.0 }
    }

    #[inline]
    pub fn length(self) -> f64 {
        (self.x * self.x + self.y * self.y + self.z * self.z).sqrt()
    }

    #[inline]
    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite() && self.z.is_finite()
    }

    #[inline]
    pub fn normalized(self) -> Self {
        let len = self.length();
        if len > 0.0 {
            self / len
        } else {
            Self::UNIT_Z
        }
    }

    #[inline]
    pub fn dot(a: Self, b: Self) -> f64 {
        a.x * b.x + a.y * b.y + a.z * b.z
    }

    #[inline]
    pub fn cross(a: Self, b: Self) -> Self {
        Self {
            x: a.y * b.z - a.z * b.y,
            y: a.z * b.x - a.x * b.z,
            z: a.x * b.y - a.y * b.x,
        }
    }

    #[inline]
    pub fn distance(a: Self, b: Self) -> f64 {
        (a - b).length()
    }
}

impl fmt::Display for Vec3 {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(
            f,
            "Vec3 {{ X = {}, Y = {}, Z = {} }}",
            self.x, self.y, self.z
        )
    }
}

impl Add for Vec3 {
    type Output = Self;
    #[inline]
    fn add(self, rhs: Self) -> Self {
        Self::new(self.x + rhs.x, self.y + rhs.y, self.z + rhs.z)
    }
}

impl Sub for Vec3 {
    type Output = Self;
    #[inline]
    fn sub(self, rhs: Self) -> Self {
        Self::new(self.x - rhs.x, self.y - rhs.y, self.z - rhs.z)
    }
}

impl Neg for Vec3 {
    type Output = Self;
    #[inline]
    fn neg(self) -> Self {
        Self::new(-self.x, -self.y, -self.z)
    }
}

impl Mul<f64> for Vec3 {
    type Output = Self;
    #[inline]
    fn mul(self, rhs: f64) -> Self {
        Self::new(self.x * rhs, self.y * rhs, self.z * rhs)
    }
}

impl Div<f64> for Vec3 {
    type Output = Self;
    #[inline]
    fn div(self, rhs: f64) -> Self {
        self * (1.0 / rhs)
    }
}

/// Column-vector affine transform; `(a * b).point(p)` applies `b` before `a`.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Affine3 {
    pub x: Vec3,
    pub y: Vec3,
    pub z: Vec3,
    pub translation: Vec3,
}

impl Default for Affine3 {
    fn default() -> Self {
        Self::IDENTITY
    }
}

impl Affine3 {
    pub const IDENTITY: Self = Self {
        x: Vec3::UNIT_X,
        y: Vec3::UNIT_Y,
        z: Vec3::UNIT_Z,
        translation: Vec3::ZERO,
    };

    #[inline]
    pub const fn new(x: Vec3, y: Vec3, z: Vec3, translation: Vec3) -> Self {
        Self {
            x,
            y,
            z,
            translation,
        }
    }

    #[inline]
    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite() && self.z.is_finite() && self.translation.is_finite()
    }

    #[inline]
    pub fn point(self, p: Vec3) -> Vec3 {
        self.direction(p) + self.translation
    }

    #[inline]
    pub fn direction(self, p: Vec3) -> Vec3 {
        self.x * p.x + self.y * p.y + self.z * p.z
    }

    #[inline]
    pub const fn move_by(p: Vec3) -> Self {
        Self {
            translation: p,
            ..Self::IDENTITY
        }
    }

    #[inline]
    pub fn scale(p: Vec3) -> Self {
        Self::new(
            Vec3::UNIT_X * p.x,
            Vec3::UNIT_Y * p.y,
            Vec3::UNIT_Z * p.z,
            Vec3::ZERO,
        )
    }

    #[inline]
    pub fn rotate_z(radians: f64) -> Self {
        let (s, c) = radians.sin_cos();
        Self::new(Vec3::xy(c, s), Vec3::xy(-s, c), Vec3::UNIT_Z, Vec3::ZERO)
    }

    /// Scaled inversion avoids cubic overflow; numerically singular transforms fail explicitly.
    pub fn try_inverse(self, relative_tolerance: f64) -> Option<Self> {
        if !self.is_finite() || !relative_tolerance.is_finite() || relative_tolerance < 0.0 {
            return None;
        }
        let max_c = |v: Vec3| v.x.abs().max(v.y.abs()).max(v.z.abs());
        let scale = max_c(self.x).max(max_c(self.y)).max(max_c(self.z));
        if !scale.is_finite() || scale <= 0.0 {
            return None;
        }
        let x = self.x / scale;
        let y = self.y / scale;
        let z = self.z / scale;
        let a = Vec3::cross(y, z);
        let b = Vec3::cross(z, x);
        let c = Vec3::cross(x, y);
        let det = Vec3::dot(x, a);
        if !det.is_finite() || det.abs() <= relative_tolerance * x.length() * y.length() * z.length()
        {
            return None;
        }
        let a = (a / det) / scale;
        let b = (b / det) / scale;
        let c = (c / det) / scale;
        let linear = Self::new(
            Vec3::new(a.x, b.x, c.x),
            Vec3::new(a.y, b.y, c.y),
            Vec3::new(a.z, b.z, c.z),
            Vec3::ZERO,
        );
        let inv = Self {
            translation: linear.direction(-self.translation),
            ..linear
        };
        inv.is_finite().then_some(inv)
    }

    /// Arbitrary-axis object coordinate system for a normal vector.
    pub fn object_coordinates(normal: Vec3) -> Self {
        let z = normal.normalized();
        let ref_axis = if z.x.abs() < 1.0 / 64.0 && z.y.abs() < 1.0 / 64.0 {
            Vec3::UNIT_Y
        } else {
            Vec3::UNIT_Z
        };
        let x = Vec3::cross(ref_axis, z).normalized();
        Self::new(x, Vec3::cross(z, x), z, Vec3::ZERO)
    }
}

impl Mul for Affine3 {
    type Output = Self;
    #[inline]
    fn mul(self, rhs: Self) -> Self {
        Self::new(
            self.direction(rhs.x),
            self.direction(rhs.y),
            self.direction(rhs.z),
            self.point(rhs.translation),
        )
    }
}

/// Axis-aligned 2D bounding box.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Bounds2 {
    pub min_x: f64,
    pub min_y: f64,
    pub max_x: f64,
    pub max_y: f64,
}

impl Default for Bounds2 {
    fn default() -> Self {
        Self::EMPTY
    }
}

impl Bounds2 {
    pub const EMPTY: Self = Self {
        min_x: f64::INFINITY,
        min_y: f64::INFINITY,
        max_x: f64::NEG_INFINITY,
        max_y: f64::NEG_INFINITY,
    };

    #[inline]
    pub const fn new(min_x: f64, min_y: f64, max_x: f64, max_y: f64) -> Self {
        Self {
            min_x,
            min_y,
            max_x,
            max_y,
        }
    }

    #[inline]
    pub fn is_empty(self) -> bool {
        self.min_x > self.max_x || self.min_y > self.max_y
    }

    #[inline]
    pub fn width(self) -> f64 {
        if self.is_empty() {
            0.0
        } else {
            self.max_x - self.min_x
        }
    }

    #[inline]
    pub fn height(self) -> f64 {
        if self.is_empty() {
            0.0
        } else {
            self.max_y - self.min_y
        }
    }

    #[inline]
    pub fn center(self) -> Vec3 {
        Vec3::xy((self.min_x + self.max_x) * 0.5, (self.min_y + self.max_y) * 0.5)
    }

    #[inline]
    pub fn include(self, p: Vec3) -> Self {
        if !p.is_finite() {
            return self;
        }
        Self::new(
            self.min_x.min(p.x),
            self.min_y.min(p.y),
            self.max_x.max(p.x),
            self.max_y.max(p.y),
        )
    }

    #[inline]
    pub fn union(self, b: Self) -> Self {
        if b.is_empty() {
            self
        } else if self.is_empty() {
            b
        } else {
            Self::new(
                self.min_x.min(b.min_x),
                self.min_y.min(b.min_y),
                self.max_x.max(b.max_x),
                self.max_y.max(b.max_y),
            )
        }
    }

    #[inline]
    pub fn inflate(self, amount: f64) -> Self {
        if self.is_empty() {
            self
        } else {
            Self::new(
                self.min_x - amount,
                self.min_y - amount,
                self.max_x + amount,
                self.max_y + amount,
            )
        }
    }

    #[inline]
    pub fn intersects(self, b: Self) -> bool {
        !self.is_empty()
            && !b.is_empty()
            && self.min_x <= b.max_x
            && self.max_x >= b.min_x
            && self.min_y <= b.max_y
            && self.max_y >= b.min_y
    }

    #[inline]
    pub fn contains(self, p: Vec3) -> bool {
        p.x >= self.min_x && p.x <= self.max_x && p.y >= self.min_y && p.y <= self.max_y
    }
}
