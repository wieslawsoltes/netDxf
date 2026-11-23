use crate::entities::{Polyline2D, Polyline2DVertex, PolylineSmoothType};

/// Low-level DXF heavy 2D `POLYLINE`/`VERTEX`/`SEQEND` record view of a `Polyline2D`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Polyline2DRecord {
    pub vertexes: Vec<Polyline2DVertex>,
    pub vertex: Option<Polyline2DVertex>,
    pub is_closed: bool,
    pub is_sequence_end: bool,
    pub uses_block_record_owner: bool,
    pub elevation: f64,
    pub smooth_type: PolylineSmoothType,
    pub layer: Option<String>,
    pub linetype: Option<String>,
    pub stored_owner: Option<String>,
    pub source_version: Option<crate::header::DxfVersion>,
    pub seqend_handle: Option<String>,
}

impl From<&Polyline2D> for Polyline2DRecord {
    fn from(poly: &Polyline2D) -> Self {
        Self {
            vertexes: poly.vertexes.clone(),
            is_closed: poly.is_closed,
            elevation: poly.elevation,
            smooth_type: PolylineSmoothType::NoSmooth,
            ..Default::default()
        }
    }
}
