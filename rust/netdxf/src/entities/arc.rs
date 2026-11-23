use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a DXF `ARC` entity (angles in degrees, CCW in OCS).
#[derive(Clone, Debug, PartialEq)]
pub struct Arc {
    pub common: EntityCommon,
    pub center: Vector3,
    pub radius: f64,
    pub start_angle: f64,
    pub end_angle: f64,
    pub thickness: f64,
}

impl Arc {
    pub fn new(center: Vector3, radius: f64, start_angle: f64, end_angle: f64) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Arc, DxfObjectCode::ARC),
            center,
            radius: radius.abs().max(f64::EPSILON),
            start_angle: MathHelper::normalize_angle(start_angle),
            end_angle: MathHelper::normalize_angle(end_angle),
            thickness: 0.0,
        }
    }

    pub fn sweep_angle(&self) -> f64 {
        let diff = MathHelper::normalize_angle(self.end_angle - self.start_angle);
        if diff == 0.0 { 360.0 } else { diff }
    }

    #[inline]
    pub fn is_counterclockwise(&self) -> bool {
        true
    }

    pub fn to_polyline2d(&self, precision: usize) -> crate::entities::Polyline2D {
        let (ocs_c, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let pts: Vec<Vector2> = self
            .polygonal_vertexes(precision)
            .into_iter()
            .map(|p| ocs_c + p)
            .collect();
        let mut pl = crate::entities::Polyline2D::from_points(pts, false);
        pl.elevation = elev;
        pl.common.normal = self.common.normal;
        pl.common.layer = self.common.layer.clone();
        pl.common.color = self.common.color;
        pl
    }

    pub fn convert_to(&self, precision: usize) -> crate::entities::Polyline2D {
        self.to_polyline2d(precision)
    }

    pub fn convert_from(center: Vector3, radius: f64, start_angle: f64, end_angle: f64) -> Self {
        Self::new(center, radius, start_angle, end_angle)
    }

    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector2> {
        let n = precision.max(2);
        let start = self.start_angle * MathHelper::DEG_TO_RAD;
        let sweep = self.sweep_angle() * MathHelper::DEG_TO_RAD;
        let step = sweep / (n - 1) as f64;
        let mut pts = Vec::with_capacity(n);
        for i in 0..n {
            let (sin, cos) = (start + step * i as f64).sin_cos();
            pts.push(Vector2::new(self.radius * cos, self.radius * sin));
        }
        pts
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        let (ocs_c, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let (s_sin, s_cos) = (self.start_angle * MathHelper::DEG_TO_RAD).sin_cos();
        let (e_sin, e_cos) = (self.end_angle * MathHelper::DEG_TO_RAD).sin_cos();
        let p_start = transformation
            * MathHelper::transform_ocs_to_wcs(
                Vector2::new(ocs_c.x + self.radius * s_cos, ocs_c.y + self.radius * s_sin),
                self.common.normal,
                elev,
            )
            + translation;
        let p_end = transformation
            * MathHelper::transform_ocs_to_wcs(
                Vector2::new(ocs_c.x + self.radius * e_cos, ocs_c.y + self.radius * e_sin),
                self.common.normal,
                elev,
            )
            + translation;
        let new_center = transformation * self.center + translation;
        let new_normal = Vector3::normalize_finite_direction(transformation * self.common.normal)
            .unwrap_or(Vector3::UNIT_Z);
        let (nc_ocs, _) = MathHelper::transform_wcs_to_ocs(new_center, new_normal);
        let (ns_ocs, _) = MathHelper::transform_wcs_to_ocs(p_start, new_normal);
        let (ne_ocs, _) = MathHelper::transform_wcs_to_ocs(p_end, new_normal);

        self.center = new_center;
        self.common.normal = new_normal;
        self.radius = Vector3::distance(new_center, p_start).max(f64::EPSILON);
        self.start_angle = MathHelper::normalize_angle(
            Vector2::angle_between_points(nc_ocs, ns_ocs) * MathHelper::RAD_TO_DEG,
        );
        self.end_angle = MathHelper::normalize_angle(
            Vector2::angle_between_points(nc_ocs, ne_ocs) * MathHelper::RAD_TO_DEG,
        );
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        let (ocs_c, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let pts = self.polygonal_vertexes(32).into_iter().map(|p| {
            MathHelper::transform_ocs_to_wcs(ocs_c + p, self.common.normal, elev).to_vector2()
        });
        BoundingRectangle::from_points(pts).unwrap_or_else(|| {
            BoundingRectangle::from_center_radius(self.center.to_vector2(), self.radius)
        })
    }
}
