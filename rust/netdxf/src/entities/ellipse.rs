use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::polyline2d::Polyline2D;
use crate::entities::polyline2d_vertex::Polyline2DVertex;
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a DXF `ELLIPSE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Ellipse {
    pub common: EntityCommon,
    pub center: Vector3,
    pub major_axis: f64,
    pub minor_axis: f64,
    pub rotation: f64,
    pub start_angle: f64,
    pub end_angle: f64,
    pub thickness: f64,
}

impl Ellipse {
    pub const MAXIMUM_DERIVATIVE_ORDER: usize = 3;
    pub const MAXIMUM_SAMPLED_VERTICES: usize = 1_000_000;

    pub fn new(center: Vector3, major_axis: f64, minor_axis: f64) -> Self {
        let (maj, min) = if major_axis >= minor_axis {
            (major_axis, minor_axis)
        } else {
            (minor_axis, major_axis)
        };
        Self {
            common: EntityCommon::new(EntityType::Ellipse, DxfObjectCode::ELLIPSE),
            center,
            major_axis: maj.abs().max(f64::EPSILON),
            minor_axis: min.abs().max(f64::EPSILON),
            rotation: 0.0,
            start_angle: 0.0,
            end_angle: 0.0,
            thickness: 0.0,
        }
    }

    #[inline]
    pub fn is_full_ellipse(&self) -> bool {
        MathHelper::is_equal(self.start_angle, self.end_angle)
            || MathHelper::is_equal((self.end_angle - self.start_angle).abs(), 360.0)
    }

    #[inline]
    pub fn is_counterclockwise(&self) -> bool {
        true
    }

    #[inline]
    pub fn minor_ratio(&self) -> f64 {
        self.minor_to_major_ratio()
    }

    #[inline]
    pub fn minor_to_major_ratio(&self) -> f64 {
        (self.minor_axis / self.major_axis).clamp(f64::EPSILON, 1.0)
    }

    pub fn set_axis(&mut self, major_axis: f64, minor_axis: f64) {
        let (maj, min) = if major_axis >= minor_axis {
            (major_axis, minor_axis)
        } else {
            (minor_axis, major_axis)
        };
        self.major_axis = maj.abs().max(f64::EPSILON);
        self.minor_axis = min.abs().max(f64::EPSILON);
    }

    /// Major axis endpoint relative to center in WCS (DXF group 11/21/31).
    pub fn end_major_axis(&self) -> Vector3 {
        self.major_axis_endpoint_wcs()
    }

    pub fn major_axis_endpoint_wcs(&self) -> Vector3 {
        let (sin, cos) = (self.rotation * MathHelper::DEG_TO_RAD).sin_cos();
        let half_major = self.major_axis * 0.5;
        MathHelper::transform_ocs_to_wcs(
            Vector2::new(half_major * cos, half_major * sin),
            self.common.normal,
            0.0,
        )
    }

    /// Computes a point on the ellipse in OCS relative to the center at `angle_deg` (degrees).
    pub fn polar_coordinate_relative_to_center(&self, angle_deg: f64) -> Vector2 {
        let a = self.major_axis * 0.5;
        let b = self.minor_axis * 0.5;
        let a1 = angle_deg * MathHelper::DEG_TO_RAD;
        let (sin_a, cos_a) = a1.sin_cos();
        let b1 = (a * sin_a).atan2(b * cos_a);
        let (sin_b, cos_b) = b1.sin_cos();
        let (sin_r, cos_r) = (self.rotation * MathHelper::DEG_TO_RAD).sin_cos();
        let px = a * cos_b;
        let py = b * sin_b;
        Vector2::new(px * cos_r - py * sin_r, px * sin_r + py * cos_r)
    }

    /// Computes a point on the ellipse in WCS at parametric angle `t_rad` (radians).
    pub fn point_at(&self, t_rad: f64) -> Vector3 {
        let a = self.major_axis * 0.5;
        let b = self.minor_axis * 0.5;
        let (sin_t, cos_t) = t_rad.sin_cos();
        let (sin_r, cos_r) = (self.rotation * MathHelper::DEG_TO_RAD).sin_cos();
        let lx = a * cos_t;
        let ly = b * sin_t;
        let ocs = Vector2::new(lx * cos_r - ly * sin_r, lx * sin_r + ly * cos_r);
        let (center_ocs, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        MathHelper::transform_ocs_to_wcs(center_ocs + ocs, self.common.normal, elev)
    }

    /// Evaluates position and derivatives up to `order` (0..=3) at parametric angle `t_rad` (radians).
    pub fn evaluate_derivatives(&self, t_rad: f64, order: usize) -> Vec<Vector3> {
        let max_order = order.min(Self::MAXIMUM_DERIVATIVE_ORDER);
        let a = self.major_axis * 0.5;
        let b = self.minor_axis * 0.5;
        let (sin_r, cos_r) = (self.rotation * MathHelper::DEG_TO_RAD).sin_cos();
        let u_axis = MathHelper::transform_ocs_to_wcs(
            Vector2::new(a * cos_r, a * sin_r),
            self.common.normal,
            0.0,
        );
        let v_axis = MathHelper::transform_ocs_to_wcs(
            Vector2::new(-b * sin_r, b * cos_r),
            self.common.normal,
            0.0,
        );
        let (sin_t, cos_t) = t_rad.sin_cos();
        let mut jet = Vec::with_capacity(max_order + 1);
        jet.push(self.center + u_axis * cos_t + v_axis * sin_t);
        if max_order >= 1 {
            jet.push(u_axis * (-sin_t) + v_axis * cos_t);
        }
        if max_order >= 2 {
            jet.push(u_axis * (-cos_t) + v_axis * (-sin_t));
        }
        if max_order >= 3 {
            jet.push(u_axis * sin_t + v_axis * (-cos_t));
        }
        jet
    }

    /// Samples the ellipse into `precision` 2D vertices in OCS.
    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector2> {
        let count = precision.clamp(3, Self::MAXIMUM_SAMPLED_VERTICES);
        let (center_ocs, _) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let mut pts = Vec::with_capacity(count);
        if self.is_full_ellipse() {
            let delta = 360.0 / count as f64;
            for i in 0..count {
                pts.push(center_ocs + self.polar_coordinate_relative_to_center(delta * i as f64));
            }
        } else {
            let mut sweep = self.end_angle - self.start_angle;
            if sweep <= 0.0 {
                sweep += 360.0;
            }
            let delta = sweep / (count - 1) as f64;
            for i in 0..count {
                pts.push(
                    center_ocs
                        + self.polar_coordinate_relative_to_center(self.start_angle + delta * i as f64),
                );
            }
        }
        pts
    }

    /// Converts the ellipse to a `Polyline2D` with `precision` segments.
    pub fn to_polyline2d(&self, precision: usize) -> Polyline2D {
        let (_, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let verts = self
            .polygonal_vertexes(precision)
            .into_iter()
            .map(Polyline2DVertex::from_position)
            .collect();
        let mut poly = Polyline2D::new(verts, self.is_full_ellipse());
        poly.common.normal = self.common.normal;
        poly.elevation = elev;
        poly.thickness = self.thickness;
        poly.common.layer = self.common.layer.clone();
        poly.common.color = self.common.color;
        poly.common.linetype = self.common.linetype.clone();
        poly
    }

    /// Alias for `to_polyline2d` matching C# `Ellipse.ConvertTo`.
    pub fn convert_to(&self, precision: usize) -> Polyline2D {
        self.to_polyline2d(precision)
    }

    /// Fits an `Ellipse` from a closed or open `Polyline2D` bounding extents (matches C# `ConvertFrom` helper).
    pub fn convert_from(polyline: &Polyline2D) -> Option<Self> {
        let bounds = polyline.bounding_rectangle()?;
        let w = bounds.width();
        let h = bounds.height();
        if w <= f64::EPSILON || h <= f64::EPSILON {
            return None;
        }
        let center_2d = bounds.center();
        let center = MathHelper::transform_ocs_to_wcs(
            center_2d,
            polyline.common.normal,
            polyline.elevation,
        );
        let mut ellipse = Self::new(center, w.max(h), w.min(h));
        if h > w {
            ellipse.rotation = 90.0;
        }
        ellipse.common.normal = polyline.common.normal;
        ellipse.thickness = polyline.thickness;
        Some(ellipse)
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        let old_major = self.major_axis_endpoint_wcs();
        let (sin, cos) = ((self.rotation + 90.0) * MathHelper::DEG_TO_RAD).sin_cos();
        let old_minor = MathHelper::transform_ocs_to_wcs(
            Vector2::new(self.minor_axis * 0.5 * cos, self.minor_axis * 0.5 * sin),
            self.common.normal,
            0.0,
        );
        let new_major = transformation * old_major;
        let new_minor = transformation * old_minor;
        self.center = transformation * self.center + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * self.common.normal) {
            self.common.normal = n;
        }
        let m1 = new_major.modulus() * 2.0;
        let m2 = new_minor.modulus() * 2.0;
        if m1 >= m2 {
            self.major_axis = m1.max(f64::EPSILON);
            self.minor_axis = m2.max(f64::EPSILON);
            let (maj_ocs, _) = MathHelper::transform_wcs_to_ocs(new_major, self.common.normal);
            self.rotation =
                MathHelper::normalize_angle(Vector2::angle(maj_ocs) * MathHelper::RAD_TO_DEG);
        } else {
            self.major_axis = m2.max(f64::EPSILON);
            self.minor_axis = m1.max(f64::EPSILON);
            let (min_ocs, _) = MathHelper::transform_wcs_to_ocs(new_minor, self.common.normal);
            self.rotation =
                MathHelper::normalize_angle(Vector2::angle(min_ocs) * MathHelper::RAD_TO_DEG);
        }
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::from_center_radius(self.center.to_vector2(), self.major_axis * 0.5)
    }
}
