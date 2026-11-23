use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::linetype_segment::LinetypeSegment;
use crate::tables::table_object::{TableObject, TableObjectData};

/// Represents a DXF linetype (`LTYPE`).
#[derive(Clone, Debug, PartialEq)]
pub struct Linetype {
    pub table: TableObjectData,
    pub description: String,
    pub segments: Vec<LinetypeSegment>,
}

impl Linetype {
    pub const BY_LAYER_NAME: &'static str = "ByLayer";
    pub const BY_BLOCK_NAME: &'static str = "ByBlock";
    pub const CONTINUOUS_NAME: &'static str = "Continuous";
    pub const DEFAULT_NAME: &'static str = "ByLayer";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::LINETYPE, true)?,
            description: String::new(),
            segments: Vec::new(),
        })
    }

    pub fn by_layer() -> Self {
        Self {
            table: TableObjectData::reserved(Self::BY_LAYER_NAME, DxfObjectCode::LINETYPE),
            description: "Linetype defined by layer.".to_string(),
            segments: Vec::new(),
        }
    }

    pub fn by_block() -> Self {
        Self {
            table: TableObjectData::reserved(Self::BY_BLOCK_NAME, DxfObjectCode::LINETYPE),
            description: "Linetype defined by block.".to_string(),
            segments: Vec::new(),
        }
    }

    pub fn continuous() -> Self {
        Self {
            table: TableObjectData::reserved(Self::CONTINUOUS_NAME, DxfObjectCode::LINETYPE),
            description: "Solid line".to_string(),
            segments: Vec::new(),
        }
    }

    pub fn dashed() -> Self {
        Self {
            table: TableObjectData::new("Dashed", DxfObjectCode::LINETYPE, false).unwrap(),
            description: "Dashed __ __ __ __ __ __ __ __ __ __ __ __ __ _".to_string(),
            segments: vec![
                LinetypeSegment::Simple { length: 0.5 },
                LinetypeSegment::Simple { length: -0.25 },
            ],
        }
    }

    pub fn center() -> Self {
        Self {
            table: TableObjectData::new("Center", DxfObjectCode::LINETYPE, false).unwrap(),
            description: "Center ____ _ ____ _ ____ _ ____ _ ____ _ ____".to_string(),
            segments: vec![
                LinetypeSegment::Simple { length: 1.25 },
                LinetypeSegment::Simple { length: -0.25 },
                LinetypeSegment::Simple { length: 0.25 },
                LinetypeSegment::Simple { length: -0.25 },
            ],
        }
    }

    pub fn dot() -> Self {
        Self {
            table: TableObjectData::new("Dot", DxfObjectCode::LINETYPE, false).unwrap(),
            description: "Dot . . . . . . . . . . . . . . . . . . . . . . . .".to_string(),
            segments: vec![
                LinetypeSegment::Simple { length: 0.0 },
                LinetypeSegment::Simple { length: -0.25 },
            ],
        }
    }

    pub fn dash_dot() -> Self {
        Self {
            table: TableObjectData::new("DashDot", DxfObjectCode::LINETYPE, false).unwrap(),
            description: "Dash dot __ . __ . __ . __ . __ . __ . __ . __".to_string(),
            segments: vec![
                LinetypeSegment::Simple { length: 0.5 },
                LinetypeSegment::Simple { length: -0.25 },
                LinetypeSegment::Simple { length: 0.0 },
                LinetypeSegment::Simple { length: -0.25 },
            ],
        }
    }

    pub fn length(&self) -> f64 {
        self.segments.iter().map(|s| s.length().abs()).sum()
    }
}

impl TableObject for Linetype {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
