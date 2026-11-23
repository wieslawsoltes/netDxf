use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::math_helper::MathHelper;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a DXF `CIRCLE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Circle {
    pub common: EntityCommon,
    pub center: Vector3,
    pub radius: f64,
    pub thickness: f64,
}

impl Circle {
    pub fn new(center: Vector3, radius: f64) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Circle, DxfObjectCode::CIRCLE),
            center,
            radius: radius.abs().max(f64::EPSILON),
            thickness: 0.0,
        }
    }

    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector2> {
        let n = precision.max(3);
        let mut pts = Vec::with_capacity(n);
        let delta = MathHelper::TWO_PI / n as f64;
        for i in 0..n {
            let (sin, cos) = (delta * i as f64).sin_cos();
            pts.push(Vector2::new(self.radius * cos, self.radius * sin));
        }
        pts
    }

    pub fn to_polyline2d(&self, precision: usize) -> crate::entities::Polyline2D {
        let (ocs_c, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let pts: Vec<Vector2> = self
            .polygonal_vertexes(precision)
            .into_iter()
            .map(|p| ocs_c + p)
            .collect();
        let mut poly = crate::entities::Polyline2D::from_points(pts, true);
        poly.common.normal = self.common.normal;
        poly.elevation = elev;
        poly
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        let new_center = transformation * self.center + translation;
        let new_normal = Vector3::normalize_finite_direction(transformation * self.common.normal)
            .unwrap_or(Vector3::UNIT_Z);
        let (ocs_c, elev) = MathHelper::transform_wcs_to_ocs(self.center, self.common.normal);
        let edge_wcs = MathHelper::transform_ocs_to_wcs(
            Vector2::new(ocs_c.x + self.radius, ocs_c.y),
            self.common.normal,
            elev,
        );
        let new_edge = transformation * edge_wcs + translation;
        self.center = new_center;
        self.common.normal = new_normal;
        self.radius = Vector3::distance(new_center, new_edge).max(f64::EPSILON);
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::from_center_radius(self.center.to_vector2(), self.radius)
    }
}
