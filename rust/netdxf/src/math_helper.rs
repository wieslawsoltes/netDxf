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

    /// Alias for `DEFAULT_EPSILON` matching C# `MathHelper.Epsilon`.
    pub const EPSILON: f64 = Self::DEFAULT_EPSILON;

    /// Converts a polyline bulge segment `(start, end, bulge)` to `(center, radius, start_angle_rad, end_angle_rad)`.
    pub fn arc_from_bulge(
        start: Vector2,
        end: Vector2,
        bulge: f64,
    ) -> Option<(Vector2, f64, f64, f64)> {
        let chord = end - start;
        let dist = chord.modulus();
        if dist <= f64::EPSILON || bulge.abs() <= f64::EPSILON {
            return None;
        }
        let theta = 4.0 * bulge.atan();
        let sin_half = (0.5 * theta).sin();
        if sin_half.abs() <= f64::EPSILON {
            return None;
        }
        let radius = (0.5 * dist / sin_half).abs();
        let mid = Vector2::new(0.5 * (start.x + end.x), 0.5 * (start.y + end.y));
        let perp = Vector2::new(-chord.y / dist, chord.x / dist);
        let offset = 0.5 * dist * (1.0 - bulge * bulge) / (2.0 * bulge);
        let center = mid + perp * offset;
        let start_rad = (start.y - center.y).atan2(start.x - center.x);
        let end_rad = start_rad + theta;
        Some((center, radius, start_rad, end_rad))
    }

    /// Computes polyline bulge `tan(sweep_rad / 4)` from an arc sweep angle in radians.
    #[inline]
    pub fn arc_to_bulge(sweep_rad: f64) -> f64 {
        (0.25 * sweep_rad).tan()
    }

    /// Finds the intersection of two 2D lines `(p0, dir0)` and `(p1, dir1)`. Returns `None` if parallel.
    pub fn find_intersection(
        p0: Vector2,
        dir0: Vector2,
        p1: Vector2,
        dir1: Vector2,
    ) -> Option<Vector2> {
        let cross = dir0.x * dir1.y - dir0.y * dir1.x;
        if Self::is_zero(cross) {
            return None;
        }
        let diff = p1 - p0;
        let t = (diff.x * dir1.y - diff.y * dir1.x) / cross;
        Some(p0 + dir0 * t)
    }

    #[inline]
    pub fn point_line_distance(p: Vector3, origin: Vector3, dir: Vector3) -> f64 {
        Self::point_line_distance_3d(p, origin, dir)
    }

    #[inline]
    pub fn point_in_segment(p: Vector3, start: Vector3, end: Vector3) -> i32 {
        Self::point_in_segment_3d(p, start, end)
    }

    #[inline]
    pub fn transform(
        point: Vector3,
        z_axis: Vector3,
        from: CoordinateSystem,
        to: CoordinateSystem,
    ) -> Vector3 {
        Self::transform_3d(point, z_axis, from, to)
    }
}
