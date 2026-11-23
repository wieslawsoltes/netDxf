use crate::dxf_object::DxfObject;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// First-class DXF `SPATIAL_FILTER` clipping boundary object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfSpatialFilter {
    pub object: DxfObject,
    pub boundary_points: Vec<Vector2>,
    pub extrusion_direction: Vector3,
    pub origin: Vector3,
    pub display_boundary: bool,
    pub front_clipping_plane: Option<f64>,
    pub back_clipping_plane: Option<f64>,
    pub inverse_block_transform: [f64; 12],
    pub block_transform: [f64; 12],
}

impl Default for DxfSpatialFilter {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfSpatialFilter {
    pub fn new() -> Self {
        let ident = [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0];
        Self {
            object: DxfObject::new("SPATIAL_FILTER"),
            boundary_points: Vec::new(),
            extrusion_direction: Vector3::UNIT_Z,
            origin: Vector3::ZERO,
            display_boundary: true,
            front_clipping_plane: None,
            back_clipping_plane: None,
            inverse_block_transform: ident,
            block_transform: ident,
        }
    }
}
