use crate::dxf_object::DxfObject;

/// First-class DXF `VBA_PROJECT` opaque binary payload object.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfVbaProject {
    pub object: DxfObject,
    pub data: Vec<u8>,
}

impl Default for DxfVbaProject {
    fn default() -> Self {
        Self::new()
    }
}

impl DxfVbaProject {
    pub const MAXIMUM_CHUNK_COUNT: usize = 65_536;
    pub const MAXIMUM_CHUNK_LENGTH: usize = 127;
    pub const MAXIMUM_DATA_LENGTH: usize = Self::MAXIMUM_CHUNK_COUNT * Self::MAXIMUM_CHUNK_LENGTH;

    pub fn new() -> Self {
        Self {
            object: DxfObject::new("VBA_PROJECT"),
            data: Vec::new(),
        }
    }

    pub fn data_length(&self) -> usize {
        self.data.len()
    }

    pub fn chunks(&self) -> Vec<Vec<u8>> {
        self.data
            .chunks(Self::MAXIMUM_CHUNK_LENGTH)
            .map(|c| c.to_vec())
            .collect()
    }

    pub fn set_chunks(&mut self, chunks: &[Vec<u8>]) -> crate::error::Result<()> {
        if chunks.len() > Self::MAXIMUM_CHUNK_COUNT {
            return Err(crate::error::DxfError::InvalidValue(
                "VBA_PROJECT chunk count exceeds maximum.".into(),
            ));
        }
        let total: usize = chunks.iter().map(Vec::len).sum();
        if total > Self::MAXIMUM_DATA_LENGTH {
            return Err(crate::error::DxfError::InvalidValue(
                "VBA_PROJECT data length exceeds maximum.".into(),
            ));
        }
        self.data.clear();
        self.data.reserve(total);
        for chunk in chunks {
            self.data.extend_from_slice(chunk);
        }
        Ok(())
    }
}
