use crate::entities::PolygonMesh;
use crate::vector3::Vector3;

/// Low-level DXF `POLYLINE`/`VERTEX`/`SEQEND` record view of a `PolygonMesh`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct PolygonMeshRecord {
    pub m: i16,
    pub n: i16,
    pub vertexes: Vec<Vector3>,
    pub is_closed_in_m: bool,
    pub is_closed_in_n: bool,
    pub seqend_handle: Option<String>,
}

impl From<&PolygonMesh> for PolygonMeshRecord {
    fn from(mesh: &PolygonMesh) -> Self {
        Self {
            m: mesh.m,
            n: mesh.n,
            vertexes: mesh.vertexes.clone(),
            is_closed_in_m: mesh.is_closed_in_m,
            is_closed_in_n: mesh.is_closed_in_n,
            seqend_handle: None,
        }
    }
}
