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

impl DimAssocOsnapPointRef {
    #[inline] pub fn marker_index(&self) -> i32 { self.gs_marker }
    #[inline] pub fn near_parameter(&self) -> f64 { self.near_param }
    #[inline] pub fn point(&self) -> Vector3 { self.osnap_point }
    #[inline] pub fn geometry(&self) -> Option<&str> { self.main_entity_handle.as_deref() }
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
    pub source_version: Option<crate::header::DxfVersion>,
    pub tags: Vec<(i32, String)>,
}

impl DxfStoredDimAssoc {
    #[inline] pub fn associativity_mask(&self) -> i32 { self.associativity_flags }
    #[inline] pub fn dimension(&self) -> Option<&str> { self.dimension_handle.as_deref() }
    #[inline] pub fn rotated_dimension_type(&self) -> i16 { self.rotated_dim_type }
    #[inline] pub fn point_references(&self) -> &[DimAssocOsnapPointRef] { &self.point_refs }
    pub fn references(&self) -> Vec<&str> {
        let mut out = Vec::new();
        if let Some(d) = self.dimension_handle.as_deref() {
            out.push(d);
        }
        for p in &self.point_refs {
            if let Some(h) = p.main_entity_handle.as_deref() {
                out.push(h);
            }
        }
        out
    }
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
            source_version: None,
            tags: Vec::new(),
        }
    }
}

/// Upstream C# `netDxf.Objects.DxfStoredDimAssocPoint` alias.
pub type DxfStoredDimAssocPoint = DimAssocOsnapPointRef;
