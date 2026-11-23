use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector2::Vector2;

/// Represents a DXF 2D `SOLID` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Solid {
    pub common: EntityCommon,
    pub first_vertex: Vector2,
    pub second_vertex: Vector2,
    pub third_vertex: Vector2,
    pub fourth_vertex: Vector2,
    pub elevation: f64,
    pub thickness: f64,
}

impl Solid {
    pub fn new(v1: Vector2, v2: Vector2, v3: Vector2, v4: Vector2) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Solid, DxfObjectCode::SOLID),
            first_vertex: v1,
            second_vertex: v2,
            third_vertex: v3,
            fourth_vertex: v4,
            elevation: 0.0,
            thickness: 0.0,
        }
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::from_points([
            self.first_vertex,
            self.second_vertex,
            self.third_vertex,
            self.fourth_vertex,
        ])
        .unwrap()
    }
}
