use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a DXF `LINE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Line {
    pub common: EntityCommon,
    pub start_point: Vector3,
    pub end_point: Vector3,
    pub thickness: f64,
}

impl Line {
    pub fn new(start_point: Vector3, end_point: Vector3) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Line, DxfObjectCode::LINE),
            start_point,
            end_point,
            thickness: 0.0,
        }
    }

    pub fn new_2d(start: Vector2, end: Vector2) -> Self {
        Self::new(start.to_vector3(0.0), end.to_vector3(0.0))
    }

    #[inline]
    pub fn direction(&self) -> Vector3 {
        self.end_point - self.start_point
    }

    #[inline]
    pub fn length(&self) -> f64 {
        Vector3::distance(self.start_point, self.end_point)
    }

    pub fn reverse(&mut self) {
        std::mem::swap(&mut self.start_point, &mut self.end_point);
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        self.start_point = transformation * self.start_point + translation;
        self.end_point = transformation * self.end_point + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * self.common.normal) {
            self.common.normal = n;
        }
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::new(self.start_point.to_vector2(), self.end_point.to_vector2())
    }

    #[inline] pub fn start(&self) -> Vector3 { self.start_point }
    #[inline] pub fn end(&self) -> Vector3 { self.end_point }

    pub fn convert_to(&self) -> crate::entities::Polyline2D {
        crate::entities::Polyline2D::from_points(
            vec![self.start_point.to_vector2(), self.end_point.to_vector2()],
            false,
        )
    }

    pub fn convert_from(start: Vector3, end: Vector3) -> Self {
        Self::new(start, end)
    }
}
