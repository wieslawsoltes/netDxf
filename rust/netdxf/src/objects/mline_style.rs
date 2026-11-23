use bitflags::bitflags;

use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::{Linetype, TableObject, TableObjectData};

bitflags! {
    /// MLine style flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct MLineStyleFlags: i16 {
        const NONE = 0;
        const FILL_ON = 1;
        const DISPLAY_JOINTS = 2;
        const START_SQUARE_END_CAP = 16;
        const START_INNER_ARCS_CAP = 32;
        const START_ROUND_OUTER_ARCS_CAP = 64;
        const END_SQUARE_END_CAP = 256;
        const END_INNER_ARCS_CAP = 512;
        const END_ROUND_OUTER_ARCS_CAP = 1024;
    }
}

/// An element line in an `MLineStyle`.
#[derive(Clone, Debug, PartialEq)]
pub struct MLineStyleElement {
    pub offset: f64,
    pub color: AciColor,
    pub linetype: String,
}

impl MLineStyleElement {
    pub fn new(offset: f64) -> Self {
        Self {
            offset,
            color: AciColor::BY_LAYER,
            linetype: Linetype::BY_LAYER_NAME.to_string(),
        }
    }
}

/// Represents a DXF `MLINESTYLE` object.
#[derive(Clone, Debug, PartialEq)]
pub struct MLineStyle {
    pub table: TableObjectData,
    pub flags: MLineStyleFlags,
    pub description: String,
    pub fill_color: AciColor,
    pub start_angle: f64,
    pub end_angle: f64,
    pub elements: Vec<MLineStyleElement>,
}

impl MLineStyle {
    pub const DEFAULT_NAME: &'static str = "Standard";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::MLINE_STYLE, true)?,
            flags: MLineStyleFlags::NONE,
            description: String::new(),
            fill_color: AciColor::BY_LAYER,
            start_angle: 90.0,
            end_angle: 90.0,
            elements: vec![MLineStyleElement::new(0.5), MLineStyleElement::new(-0.5)],
        })
    }

    pub fn default_style() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::MLINE_STYLE),
            flags: MLineStyleFlags::NONE,
            description: String::new(),
            fill_color: AciColor::BY_LAYER,
            start_angle: 90.0,
            end_angle: 90.0,
            elements: vec![MLineStyleElement::new(0.5), MLineStyleElement::new(-0.5)],
        }
    }
}

impl TableObject for MLineStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
