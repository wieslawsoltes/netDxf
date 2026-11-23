use crate::entities::{PolyfaceMesh, PolyfaceMeshFace};
use crate::vector3::Vector3;

/// Low-level DXF `POLYLINE`/`VERTEX`/`SEQEND` record view of a `PolyfaceMesh`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct PolyfaceMeshRecord {
    pub vertexes: Vec<Vector3>,
    pub faces: Vec<PolyfaceMeshFace>,
    pub seqend_handle: Option<String>,
}

impl From<&PolyfaceMesh> for PolyfaceMeshRecord {
    fn from(mesh: &PolyfaceMesh) -> Self {
        Self {
            vertexes: mesh.vertexes.clone(),
            faces: mesh.faces.clone(),
            seqend_handle: None,
        }
    }
}
