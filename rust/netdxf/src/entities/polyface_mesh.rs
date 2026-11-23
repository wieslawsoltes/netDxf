use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::{EntityCommon, EntityObject};
use crate::entities::entity_type::EntityType;
use crate::entities::face3d::{Face3D, Face3DEdgeFlags};
use crate::matrix3::Matrix3;
use crate::vector3::Vector3;

/// Represents a face in a `PolyfaceMesh` (1-based vertex indices; negative means invisible edge).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct PolyfaceMeshFace {
    pub vertex_indexes: Vec<i16>,
    pub face_color: Option<crate::AciColor>,
    pub face_layer: Option<String>,
}

impl PolyfaceMeshFace {
    pub fn new(vertex_indexes: Vec<i16>) -> Self {
        Self {
            vertex_indexes,
            face_color: None,
            face_layer: None,
        }
    }

    pub fn color(&self) -> Option<crate::AciColor> {
        self.face_color
    }

    pub fn layer(&self) -> Option<&str> {
        self.face_layer.as_deref()
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

    #[inline]
    pub fn declared_vertex_count(&self) -> usize {
        self.vertexes.len()
    }

    #[inline]
    pub fn declared_face_count(&self) -> usize {
        self.faces.len()
    }

    #[inline]
    pub fn face_records(&self) -> &[PolyfaceMeshFace] {
        &self.faces
    }

    #[inline]
    pub fn vertex_records(&self) -> &[Vector3] {
        &self.vertexes
    }

    #[inline]
    pub fn record_sequence(&self) -> (&[Vector3], &[PolyfaceMeshFace]) {
        (&self.vertexes, &self.faces)
    }

    #[inline]
    pub fn end_sequence_record(&self) -> Option<&str> {
        None
    }

    pub fn set_vertex(&mut self, index: usize, point: Vector3) -> bool {
        if let Some(slot) = self.vertexes.get_mut(index) {
            *slot = point;
            true
        } else {
            false
        }
    }

    /// Updates a 1-based vertex index on face `face_idx` at corner `corner_idx`, preserving visibility sign.
    pub fn set_face_vertex_index(
        &mut self,
        face_idx: usize,
        corner_idx: usize,
        vertex_index_1based: i16,
    ) -> bool {
        if let Some(face) = self.faces.get_mut(face_idx) {
            if let Some(slot) = face.vertex_indexes.get_mut(corner_idx) {
                let sign = if *slot < 0 { -1 } else { 1 };
                *slot = sign * vertex_index_1based.abs();
                return true;
            }
        }
        false
    }

    /// Sets edge visibility (`true` = visible / positive index, `false` = hidden / negative index).
    pub fn set_face_edge_visibility(
        &mut self,
        face_idx: usize,
        corner_idx: usize,
        visible: bool,
    ) -> bool {
        if let Some(face) = self.faces.get_mut(face_idx) {
            if let Some(slot) = face.vertex_indexes.get_mut(corner_idx) {
                let mag = slot.abs();
                *slot = if visible { mag } else { -mag };
                return true;
            }
        }
        false
    }

    /// Explodes the polyface mesh into `Face3D` entities preserving invisible edge flags.
    pub fn explode(&self) -> Vec<EntityObject> {
        let mut out = Vec::with_capacity(self.faces.len());
        for face in &self.faces {
            if face.vertex_indexes.len() < 3 {
                continue;
            }
            let mut pts = Vec::with_capacity(4);
            let mut flags = Face3DEdgeFlags::NONE;
            for (corner, &raw_idx) in face.vertex_indexes.iter().take(4).enumerate() {
                if raw_idx == 0 {
                    continue;
                }
                if raw_idx < 0 {
                    flags |= match corner {
                        0 => Face3DEdgeFlags::FIRST,
                        1 => Face3DEdgeFlags::SECOND,
                        2 => Face3DEdgeFlags::THIRD,
                        3 => Face3DEdgeFlags::FOURTH,
                        _ => Face3DEdgeFlags::NONE,
                    };
                }
                let idx = (raw_idx.unsigned_abs() as usize).saturating_sub(1);
                if let Some(&pt) = self.vertexes.get(idx) {
                    pts.push(pt);
                }
            }
            if pts.len() >= 3 {
                let p3 = pts.get(3).copied().unwrap_or(pts[2]);
                let mut f = Face3D::new(pts[0], pts[1], pts[2], p3);
                f.edge_flags = flags;
                f.common.layer = self.common.layer.clone();
                f.common.color = self.common.color;
                out.push(EntityObject::Face3D(Box::new(f)));
            }
        }
        out
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
