use crate::io::dxf_tag::DxfTag;

/// A single `(0, NAME)` DXF record inside a `DxfRawSection` (`netDxf.IO.DxfRawRecord`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfRawRecord {
    pub name: String,
    pub start_tag_index: usize,
    pub end_tag_index: usize,
    pub tags: Vec<DxfTag>,
}

impl DxfRawRecord {
    pub fn handle(&self) -> Option<&str> {
        self.tags
            .iter()
            .find(|t| t.code == 5 || t.code == 105)
            .map(|t| t.value.as_str())
    }
}
