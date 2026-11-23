use std::f64::consts::{FRAC_PI_2, PI, TAU};

use crate::coordinate_system::CoordinateSystem;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Utility math functions and constants matching `netDxf.MathHelper`.
pub struct MathHelper;

impl MathHelper {
    pub const DEG_TO_RAD: f64 = PI / 180.0;
    pub const RAD_TO_DEG: f64 = 180.0 / PI;
    pub const DEG_TO_GRAD: f64 = 10.0 / 9.0;
    pub const GRAD_TO_DEG: f64 = 9.0 / 10.0;
    pub const HALF_PI: f64 = FRAC_PI_2;
    pub const PI: f64 = PI;
    pub const THREE_HALF_PI: f64 = 3.0 * FRAC_PI_2;
    pub const TWO_PI: f64 = TAU;
    pub const DEFAULT_EPSILON: f64 = 1e-12;

    #[inline]
    pub fn sign(number: f64) -> i32 {
        Self::sign_with(number, Self::DEFAULT_EPSILON)
    }

    #[inline]
    pub fn sign_with(number: f64, threshold: f64) -> i32 {
        if Self::is_zero_with(number, threshold) {
            0
        } else if number > 0.0 {
            1
        } else {
            -1
        }
    }

    #[inline]
    pub fn is_one(number: f64) -> bool {
        Self::is_one_with(number, Self::DEFAULT_EPSILON)
    }

    #[inline]
    pub fn is_one_with(number: f64, threshold: f64) -> bool {
        Self::is_zero_with(number - 1.0, threshold)
    }

    #[inline]
    pub fn is_zero(number: f64) -> bool {
        Self::is_zero_with(number, Self::DEFAULT_EPSILON)
    }

    #[inline]
    pub fn is_zero_with(number: f64, threshold: f64) -> bool {
        number >= -threshold && number <= threshold
    }

    #[inline]
    pub fn is_equal(a: f64, b: f64) -> bool {
        Self::is_equal_with(a, b, Self::DEFAULT_EPSILON)
    }

    #[inline]
    pub fn is_equal_with(a: f64, b: f64, threshold: f64) -> bool {
        Self::is_zero_with(a - b, threshold)
    }

    /// Normalizes an angle in degrees into `[0.0, 360.0)`.
    #[inline]
    pub fn normalize_angle(angle: f64) -> f64 {
        let mut normalized = angle % 360.0;
        if Self::is_zero(normalized) || Self::is_equal(normalized.abs(), 360.0) {
            return 0.0;
        }
        if normalized < 0.0 {
            normalized += 360.0;
        }
        normalized
    }

    #[inline]
    pub fn round_to_nearest(number: f64, round_to: f64) -> f64 {
        (number / round_to).round() * round_to
    }

    /// AutoCAD Arbitrary Axis Algorithm: computes the right-handed OCS-to-WCS rotation matrix,
    /// returning an error if `z_axis` is zero or non-finite.
    pub fn try_arbitrary_axis(z_axis: Vector3) -> crate::Result<Matrix3> {
        let z = Vector3::normalize_finite_direction(z_axis).ok_or_else(|| {
            crate::DxfError::InvalidValue(
                "z_axis must be a finite, non-zero direction vector".to_string(),
            )
        })?;
        if z.x == 0.0 && z.y == 0.0 && z.z == 1.0 {
            return Ok(Matrix3::IDENTITY);
        }

        let ax_raw = if z.x.abs() < (1.0 / 64.0) && z.y.abs() < (1.0 / 64.0) {
            Vector3::cross_product(Vector3::UNIT_Y, z)
        } else {
            Vector3::cross_product(Vector3::UNIT_Z, z)
        };
        let ax = Vector3::normalize_finite_direction(ax_raw).unwrap_or(Vector3::UNIT_X);
        let ay = Vector3::normalize_finite_direction(Vector3::cross_product(z, ax))
            .unwrap_or(Vector3::UNIT_Y);

        Ok(Matrix3::new(ax.x, ay.x, z.x, ax.y, ay.y, z.y, ax.z, ay.z, z.z))
    }

    /// AutoCAD Arbitrary Axis Algorithm: computes the right-handed OCS-to-WCS rotation matrix.
    /// Falls back to `Vector3::UNIT_Z` if `z_axis` is degenerate.
    pub fn arbitrary_axis(z_axis: Vector3) -> Matrix3 {
        Self::try_arbitrary_axis(z_axis).unwrap_or(Matrix3::IDENTITY)
    }

    #[inline]
    pub fn transform_2d(
        point: Vector2,
        rotation: f64,
        from: CoordinateSystem,
        to: CoordinateSystem,
    ) -> Vector2 {
        if Self::is_zero(rotation) {
            return point;
        }
        let (sin, cos) = rotation.sin_cos();
        match (from, to) {
            (CoordinateSystem::World, CoordinateSystem::Object) => {
                Vector2::new(point.x * cos + point.y * sin, -point.x * sin + point.y * cos)
            }
            (CoordinateSystem::Object, CoordinateSystem::World) => {
                Vector2::new(point.x * cos - point.y * sin, point.x * sin + point.y * cos)
            }
            _ => point,
        }
    }

    #[inline]
    pub fn transform_3d(
        point: Vector3,
        z_axis: Vector3,
        from: CoordinateSystem,
        to: CoordinateSystem,
    ) -> Vector3 {
        let trans = Self::arbitrary_axis(z_axis);
        match (from, to) {
            (CoordinateSystem::World, CoordinateSystem::Object) => trans.transpose() * point,
            (CoordinateSystem::Object, CoordinateSystem::World) => trans * point,
            _ => point,
        }
    }

    /// Transforms a 2D OCS point with `elevation` to a 3D WCS point.
    #[inline]
    pub fn transform_ocs_to_wcs(point: Vector2, z_axis: Vector3, elevation: f64) -> Vector3 {
        Self::arbitrary_axis(z_axis) * Vector3::new(point.x, point.y, elevation)
    }

    /// Transforms a 3D WCS point to a 2D OCS point and elevation.
    #[inline]
    pub fn transform_wcs_to_ocs(point: Vector3, z_axis: Vector3) -> (Vector2, f64) {
        let p = Self::arbitrary_axis(z_axis).transpose() * point;
        (Vector2::new(p.x, p.y), p.z)
    }

    #[inline]
    pub fn point_line_distance_3d(p: Vector3, origin: Vector3, dir: Vector3) -> f64 {
        let t = Vector3::dot_product(dir, p - origin);
        (p - (origin + dir * t)).modulus()
    }

    #[inline]
    pub fn point_line_distance_2d(p: Vector2, origin: Vector2, dir: Vector2) -> f64 {
        let t = Vector2::dot_product(dir, p - origin);
        (p - (origin + dir * t)).modulus()
    }

    #[inline]
    pub fn point_in_segment_3d(p: Vector3, start: Vector3, end: Vector3) -> i32 {
        let dir = end - start;
        let t = Vector3::dot_product(dir, p - start);
        if t < 0.0 {
            return -1;
        }
        if t > Vector3::dot_product(dir, dir) {
            return 1;
        }
        0
    }

    #[inline]
    pub fn point_in_segment_2d(p: Vector2, start: Vector2, end: Vector2) -> i32 {
        let dir = end - start;
        let t = Vector2::dot_product(dir, p - start);
        if t < 0.0 {
            return -1;
        }
        if t > Vector2::dot_product(dir, dir) {
            return 1;
        }
        0
    }
}
