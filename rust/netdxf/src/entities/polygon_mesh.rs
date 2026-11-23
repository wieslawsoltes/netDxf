use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::polyline2d::PolylineSmoothType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF `POLYLINE` 3D polygon mesh entity.
#[derive(Clone, Debug, PartialEq)]
pub struct PolygonMesh {
    pub common: EntityCommon,
    pub m: i16,
    pub n: i16,
    pub vertexes: Vec<Vector3>,
    pub is_closed_in_m: bool,
    pub is_closed_in_n: bool,
    pub smooth_type: PolylineSmoothType,
    pub density_m: i16,
    pub density_n: i16,
}

impl PolygonMesh {
    pub fn new(m: i16, n: i16, vertexes: Vec<Vector3>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::PolygonMesh, DxfObjectCode::POLYLINE),
            m,
            n,
            vertexes,
            is_closed_in_m: false,
            is_closed_in_n: false,
            smooth_type: PolylineSmoothType::NoSmooth,
            density_m: 0,
            density_n: 0,
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
