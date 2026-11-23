use crate::vector3::Vector3;

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
    pub defined_height: f64,
    pub stored_total_width: Option<f64>,
    pub heights: Vec<f64>,
    pub embedded_insertion_point: Option<Vector3>,
    pub embedded_reference_width: Option<f64>,
    pub embedded_text_direction: Option<Vector3>,
    pub linked_columns: Vec<String>,
}

impl MTextColumns {
    #[inline]
    pub fn column_type(&self) -> MTextColumnType {
        self.column_type
    }

    /// Computes total width across all columns including gutters.
    pub fn total_width(&self) -> f64 {
        if let Some(w) = self.stored_total_width {
            return w;
        }
        let n = self.count.max(0) as f64;
        if n <= 0.0 {
            0.0
        } else {
            n * self.width + (n - 1.0) * self.gutter
        }
    }

    /// Computes maximum column height across all columns.
    pub fn total_height(&self) -> f64 {
        if self.heights.is_empty() {
            self.defined_height
        } else {
            self.heights
                .iter()
                .copied()
                .fold(0.0_f64, |acc, h| acc.max(h))
        }
    }

    /// Resets embedded column placement overrides back to `None`.
    pub fn reset_embedded_placement(&mut self) {
        self.embedded_insertion_point = None;
        self.embedded_reference_width = None;
        self.embedded_text_direction = None;
    }
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
            defined_height: 0.0,
            stored_total_width: None,
            heights: Vec::new(),
            embedded_insertion_point: None,
            embedded_reference_width: None,
            embedded_text_direction: None,
            linked_columns: Vec::new(),
        }
    }
}
