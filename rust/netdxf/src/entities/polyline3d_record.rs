use crate::entities::{Polyline3D, PolylineSmoothType};
use crate::vector3::Vector3;

/// Low-level DXF 3D `POLYLINE`/`VERTEX`/`SEQEND` record view of a `Polyline3D`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Polyline3DRecord {
    pub vertexes: Vec<Vector3>,
    pub is_closed: bool,
    pub smooth_type: PolylineSmoothType,
    pub seqend_handle: Option<String>,
}

impl From<&Polyline3D> for Polyline3DRecord {
    fn from(poly: &Polyline3D) -> Self {
        Self {
            vertexes: poly.vertexes.clone(),
            is_closed: poly.is_closed,
            smooth_type: poly.smooth_type,
            seqend_handle: None,
        }
    }
}
