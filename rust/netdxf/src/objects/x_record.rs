use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::DxfTag;

/// Represents a DXF `XRECORD` object.
#[derive(Clone, Debug, PartialEq)]
pub struct XRecord {
    pub object: DxfObject,
    pub cloning_flags: i16,
    pub entries: Vec<DxfTag>,
    pub schema_managed: bool,
}

impl XRecord {
    pub fn new() -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::XRECORD),
            cloning_flags: 1,
            entries: Vec::new(),
            schema_managed: false,
        }
    }

    #[inline] pub fn cloning(&self) -> i16 { self.cloning_flags }
    #[inline] pub fn data(&self) -> &[DxfTag] { &self.entries }
    #[inline] pub fn is_schema_managed(&self) -> bool { self.schema_managed }
}

impl Default for XRecord {
    fn default() -> Self {
        Self::new()
    }
}

/// Upstream C# `netDxf.Objects.DxfXRecord` alias.
pub type DxfXRecord = XRecord;
