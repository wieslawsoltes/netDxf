/// Column layout type for an `MText` entity (DXF group 75).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum MTextColumnType {
    #[default]
    None = 0,
    Static = 1,
    Dynamic = 2,
}

/// Storage encoding for `MText` column metadata.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub enum MTextColumnStorage {
    #[default]
    Direct,
    LegacyLinked,
    Embedded,
}

/// Stored column layout settings for an `MText` entity.
#[derive(Debug, Clone, PartialEq)]
pub struct MTextColumns {
    pub column_type: MTextColumnType,
    pub storage: MTextColumnStorage,
    pub count: i32,
    pub flow_reversed: bool,
    pub auto_height: bool,
    pub width: f64,
    pub gutter: f64,
    pub heights: Vec<f64>,
}

impl Default for MTextColumns {
    fn default() -> Self {
        Self {
            column_type: MTextColumnType::None,
            storage: MTextColumnStorage::Direct,
            count: 0,
            flow_reversed: false,
            auto_height: true,
            width: 0.0,
            gutter: 0.0,
            heights: Vec::new(),
        }
    }
}
