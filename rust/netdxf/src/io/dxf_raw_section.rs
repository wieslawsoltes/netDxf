use crate::io::dxf_raw_record::DxfRawRecord;

/// A `0 SECTION / 2 <NAME> ... 0 ENDSEC` span inside a `DxfRawDocument` (`netDxf.IO.DxfRawSection`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfRawSection {
    pub name: String,
    pub start_tag_index: usize,
    pub end_tag_index: usize,
    pub records: Vec<DxfRawRecord>,
}
