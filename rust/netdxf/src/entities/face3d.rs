use bitflags::bitflags;

use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

bitflags! {
    /// Edge visibility flags for `3DFACE` (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct Face3DEdgeFlags: i16 {
        const NONE = 0;
        const FIRST = 1;
        const SECOND = 2;
        const THIRD = 4;
        const FOURTH = 8;
    }
}

/// Represents a DXF `3DFACE` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Face3D {
    pub common: EntityCommon,
    pub first_vertex: Vector3,
    pub second_vertex: Vector3,
    pub third_vertex: Vector3,
    pub fourth_vertex: Vector3,
    pub edge_flags: Face3DEdgeFlags,
}

impl Face3D {
    pub fn new(v1: Vector3, v2: Vector3, v3: Vector3, v4: Vector3) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Face3D, DxfObjectCode::FACE_3D),
            first_vertex: v1,
            second_vertex: v2,
            third_vertex: v3,
            fourth_vertex: v4,
            edge_flags: Face3DEdgeFlags::NONE,
        }
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        self.first_vertex = transformation * self.first_vertex + translation;
        self.second_vertex = transformation * self.second_vertex + translation;
        self.third_vertex = transformation * self.third_vertex + translation;
        self.fourth_vertex = transformation * self.fourth_vertex + translation;
    }

    pub fn bounding_rectangle(&self) -> BoundingRectangle {
        BoundingRectangle::from_points([
            self.first_vertex.to_vector2(),
            self.second_vertex.to_vector2(),
            self.third_vertex.to_vector2(),
            self.fourth_vertex.to_vector2(),
        ])
        .unwrap()
    }
}
