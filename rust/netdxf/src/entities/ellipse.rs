use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
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
    pub fn minor_to_major_ratio(&self) -> f64 {
        (self.minor_axis / self.major_axis).clamp(f64::EPSILON, 1.0)
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
            self.rotation = MathHelper::normalize_angle(Vector2::angle(maj_ocs) * MathHelper::RAD_TO_DEG);
        } else {
            self.major_axis = m2.max(f64::EPSILON);
            self.minor_axis = m1.max(f64::EPSILON);
            let (min_ocs, _) = MathHelper::transform_wcs_to_ocs(new_minor, self.common.normal);
            self.rotation = MathHelper::normalize_angle(Vector2::angle(min_ocs) * MathHelper::RAD_TO_DEG);
        }
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::from_center_radius(self.center.to_vector2(), self.major_axis * 0.5)
    }
}
