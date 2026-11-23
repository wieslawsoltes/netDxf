use crate::io::dxf_raw_record::DxfRawRecord;

/// A `0 SECTION / 2 <NAME> ... 0 ENDSEC` span inside a `DxfRawDocument` (`netDxf.IO.DxfRawSection`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfRawSection {
    pub name: String,
    pub start_tag_index: usize,
    pub end_tag_index: usize,
    pub records: Vec<DxfRawRecord>,
}

impl DxfRawSection {
    #[inline]
    pub fn content(&self) -> &[DxfRawRecord] {
        &self.records
    }

    #[inline]
    pub fn content_start_tag_index(&self) -> usize {
        self.start_tag_index + 2
    }

    #[inline]
    pub fn preamble(&self) -> usize {
        self.start_tag_index
    }
}
