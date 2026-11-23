use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents an edge in a `Mesh` entity.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct MeshEdge {
    pub start_vertex_index: i32,
    pub end_vertex_index: i32,
    pub crease: f64,
}

/// Represents a DXF `MESH` (subdivision mesh) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Mesh {
    pub common: EntityCommon,
    pub vertexes: Vec<Vector3>,
    pub faces: Vec<Vec<i32>>,
    pub edges: Vec<MeshEdge>,
    pub subdivision_level: u8,
}

impl Mesh {
    pub fn new(vertexes: Vec<Vector3>, faces: Vec<Vec<i32>>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Mesh, DxfObjectCode::MESH),
            vertexes,
            faces,
            edges: Vec::new(),
            subdivision_level: 0,
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
