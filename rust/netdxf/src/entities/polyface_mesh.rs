use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a face in a `PolyfaceMesh` (1-based vertex indices; negative means invisible edge).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PolyfaceMeshFace {
    pub vertex_indexes: Vec<i16>,
}

impl PolyfaceMeshFace {
    pub fn new(vertex_indexes: Vec<i16>) -> Self {
        Self { vertex_indexes }
    }
}

/// Represents a DXF `POLYLINE` polyface mesh entity.
#[derive(Clone, Debug, PartialEq)]
pub struct PolyfaceMesh {
    pub common: EntityCommon,
    pub vertexes: Vec<Vector3>,
    pub faces: Vec<PolyfaceMeshFace>,
}

impl PolyfaceMesh {
    pub fn new(vertexes: Vec<Vector3>, faces: Vec<PolyfaceMeshFace>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::PolyfaceMesh, DxfObjectCode::POLYLINE),
            vertexes,
            faces,
        }
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        for v in &mut self.vertexes {
            *v = transformation * *v + translation;
        }
    }

    pub fn bounding_rectangle(&self) -> Option<BoundingRectangle> {
        BoundingRectangle::from_points(self.vertexes.iter().map(|v| v.to_vector2()))
    }
}
