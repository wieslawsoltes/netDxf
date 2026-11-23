use crate::dxf_object::DxfObject;
use crate::io::dxf_tag::DxfTag;

/// Lossless tag-level container for unrecognized or unsupported `OBJECTS` section records (`netDxf.Objects.DxfOpaqueObject`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfOpaqueObject {
    pub object: DxfObject,
    pub tags: Vec<DxfTag>,
}

impl DxfOpaqueObject {
    pub fn new(code_name: impl Into<String>, tags: Vec<DxfTag>) -> Self {
        Self {
            object: DxfObject::new(code_name),
            tags,
        }
    }
}
