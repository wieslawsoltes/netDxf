use crate::entities::{PolyfaceMesh, PolyfaceMeshFace};
use crate::vector3::Vector3;

/// Low-level DXF `POLYLINE`/`VERTEX`/`SEQEND` record view of a `PolyfaceMesh`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct PolyfaceMeshRecord {
    pub vertexes: Vec<Vector3>,
    pub faces: Vec<PolyfaceMeshFace>,
    pub face: Option<PolyfaceMeshFace>,
    pub is_face_record: bool,
    pub is_sequence_end: bool,
    pub uses_block_record_owner: bool,
    pub layer: Option<String>,
    pub linetype: Option<String>,
    pub stored_owner: Option<String>,
    pub source_version: Option<crate::header::DxfVersion>,
    pub seqend_handle: Option<String>,
}

impl From<&PolyfaceMesh> for PolyfaceMeshRecord {
    fn from(mesh: &PolyfaceMesh) -> Self {
        Self {
            vertexes: mesh.vertexes.clone(),
            faces: mesh.faces.clone(),
            ..Default::default()
        }
    }
}
