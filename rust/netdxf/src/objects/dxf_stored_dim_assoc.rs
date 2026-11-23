use crate::dxf_object::DxfObject;
use crate::vector3::Vector3;

/// Associative osnap point reference inside a `DIMASSOC` object.
#[derive(Clone, Debug, PartialEq)]
pub struct DimAssocOsnapPointRef {
    pub osnap_type: i16,
    pub main_entity_handle: Option<String>,
    pub subentity_type: i16,
    pub gs_marker: i32,
    pub osnap_point: Vector3,
    pub near_param: f64,
}

impl Default for DimAssocOsnapPointRef {
    fn default() -> Self {
        Self {
            osnap_type: 0,
            main_entity_handle: None,
            subentity_type: 0,
            gs_marker: 0,
            osnap_point: Vector3::ZERO,
            near_param: 0.0,
        }
    }
}

/// Represents a DXF `DIMASSOC` associative dimension object (`netDxf.Objects.DxfStoredDimAssoc`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfStoredDimAssoc {
    pub object: DxfObject,
    pub dimension_handle: Option<String>,
    pub associativity_flags: i32,
    pub trans_space: bool,
    pub rotated_dim_type: i16,
    pub point_refs: Vec<DimAssocOsnapPointRef>,
}

impl Default for DxfStoredDimAssoc {
    fn default() -> Self {
        Self {
            object: DxfObject::new("DIMASSOC"),
            dimension_handle: None,
            associativity_flags: 0,
            trans_space: false,
            rotated_dim_type: 0,
            point_refs: Vec::new(),
        }
    }
}

/// Upstream C# `netDxf.Objects.DxfStoredDimAssocPoint` alias.
pub type DxfStoredDimAssocPoint = DimAssocOsnapPointRef;
