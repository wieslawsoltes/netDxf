use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::polyline2d::PolylineSmoothType;
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a DXF 3D polyline (`POLYLINE` + `VERTEX` + `SEQEND`).
#[derive(Clone, Debug, PartialEq)]
pub struct Polyline3D {
    pub common: EntityCommon,
    pub vertexes: Vec<Vector3>,
    pub is_closed: bool,
    pub smooth_type: PolylineSmoothType,
    pub linetype_generation: bool,
}

impl Polyline3D {
    pub fn new(vertexes: Vec<Vector3>, is_closed: bool) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Polyline3D, DxfObjectCode::POLYLINE),
            vertexes,
            is_closed,
            smooth_type: PolylineSmoothType::NoSmooth,
            linetype_generation: false,
        }
    }

    pub fn reverse(&mut self) {
        self.vertexes.reverse();
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
