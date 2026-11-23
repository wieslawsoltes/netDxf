use crate::error::{DxfError, Result};

/// Resource limits for explicitly materialized dimension-picture exports.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfDimensionPictureOptions {
    pub maximum_pictures: usize,
    pub maximum_picture_entities: usize,
}

impl Default for DxfDimensionPictureOptions {
    fn default() -> Self {
        Self {
            maximum_pictures: 10_000,
            maximum_picture_entities: 200_000,
        }
    }
}

impl DxfDimensionPictureOptions {
    #[inline]
    pub fn raw_options(&self) -> &Self {
        self
    }

    pub fn new(maximum_pictures: usize, maximum_picture_entities: usize) -> Result<Self> {
        if maximum_pictures < 1 {
            return Err(DxfError::InvalidValue(
                "maximum_pictures must be at least 1.".into(),
            ));
        }
        if maximum_picture_entities < 1 {
            return Err(DxfError::InvalidValue(
                "maximum_picture_entities must be at least 1.".into(),
            ));
        }
        Ok(Self {
            maximum_pictures,
            maximum_picture_entities,
        })
    }
}
