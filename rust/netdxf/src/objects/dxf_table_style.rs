use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::{TableObject, TableObjectData, TextStyle};

/// Represents a DXF `TABLESTYLE` object (`netDxf.Objects.DxfTableStyle`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfTableStyle {
    pub table: TableObjectData,
    pub description: String,
    pub flow_direction: i16,
    pub horizontal_cell_margin: f64,
    pub vertical_cell_margin: f64,
    pub suppress_title: bool,
    pub suppress_header: bool,
    pub text_style: String,
    pub text_height: f64,
    pub text_color: AciColor,
    pub grid_color: AciColor,
}

impl DxfTableStyle {
    pub const DEFAULT_NAME: &'static str = "Standard";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        let mut s = Self::default_style();
        s.table = TableObjectData::new(name, DxfObjectCode::DICTIONARY, true)?;
        s.table.object.code_name = "TABLESTYLE".to_string();
        Ok(s)
    }

    pub fn default_style() -> Self {
        let mut table = TableObjectData::reserved(Self::DEFAULT_NAME, "TABLESTYLE");
        table.object.code_name = "TABLESTYLE".to_string();
        Self {
            table,
            description: String::new(),
            flow_direction: 0,
            horizontal_cell_margin: 0.06,
            vertical_cell_margin: 0.06,
            suppress_title: false,
            suppress_header: false,
            text_style: TextStyle::DEFAULT_NAME.to_string(),
            text_height: 0.18,
            text_color: AciColor::BY_BLOCK,
            grid_color: AciColor::BY_BLOCK,
        }
    }
}

impl TableObject for DxfTableStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}

/// Border conflict resolution policy for [`DxfTableStyle`] layout generation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub enum DxfTableBorderConflictPolicy {
    #[default]
    Reject,
    LastCell,
}

/// Frame kind referenced by a [`DxfTableContentStyleReference`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum DxfTableContentStyleReferenceKind {
    Column,
    Row,
    Cell,
}

/// Value kind stored inside a `TABLECONTENT` cell value (DXF group 90).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(i32)]
pub enum DxfStoredTableContentValueKind {
    Integer = 1,
    Double = 2,
    String = 4,
    Point3D = 32,
}

/// Table style header metadata (`TABLESTYLE`).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableStyleHeader {
    pub description: String,
    pub flow_direction: i16,
    pub flags: i16,
    pub horizontal_cell_margin: f64,
    pub vertical_cell_margin: f64,
    pub suppress_title: bool,
    pub suppress_header: bool,
}

/// Border style values for a table style row.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableStyleBorderValues {
    pub lineweight: i16,
    pub visible: bool,
    pub color: Option<crate::aci_color::AciColor>,
}

/// All 6 border definitions for a table style row.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableStyleRowBorders {
    pub top: DxfTableStyleBorderValues,
    pub horizontal_inside: DxfTableStyleBorderValues,
    pub bottom: DxfTableStyleBorderValues,
    pub left: DxfTableStyleBorderValues,
    pub vertical_inside: DxfTableStyleBorderValues,
    pub right: DxfTableStyleBorderValues,
}

/// Data type and unit type pair for a table style row.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DxfTableStyleRowDataTypes {
    pub data_type: i32,
    pub unit_type: i32,
}

/// Row style definition inside a [`DxfTableStyle`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableStyleRow {
    pub text_style: String,
    pub text_height: f64,
    pub alignment: i16,
    pub text_color: Option<crate::aci_color::AciColor>,
    pub fill_color: Option<crate::aci_color::AciColor>,
    pub fill_enabled: bool,
    pub borders: DxfTableStyleRowBorders,
    pub data_types: DxfTableStyleRowDataTypes,
}

/// Edit and value snapshot aliases for [`DxfTableStyleRow`].
pub type DxfTableStyleRowEdit = DxfTableStyleRow;
pub type DxfTableStyleRowValues = DxfTableStyleRow;

/// Rectangular range of table cells `(top_row..=bottom_row, left_col..=right_col)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DxfTableCellRange {
    pub top_row: usize,
    pub left_column: usize,
    pub bottom_row: usize,
    pub right_column: usize,
}

/// Resolved 2D bounding layout for a single table cell.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfTableCellLayout {
    pub row: usize,
    pub column: usize,
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
}

/// Row or column band metadata in `TABLECONTENT`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableContentBand {
    pub index: usize,
    pub size: f64,
    pub cell_style_id: Option<i32>,
}

/// Style reference from a column, row, or cell in `TABLECONTENT`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DxfTableContentStyleReference {
    pub kind: DxfTableContentStyleReferenceKind,
    pub row: usize,
    pub column: usize,
    pub style_id: i32,
}

/// Formatting options attached to a [`DxfStoredTableContentValue`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfStoredTableContentValueFormatting {
    pub data_type: i32,
    pub unit_type: i32,
    pub format_string: String,
}

/// Stored typed value inside a `TABLECONTENT` cell.
#[derive(Debug, Clone, PartialEq)]
pub struct DxfStoredTableContentValue {
    pub kind: DxfStoredTableContentValueKind,
    pub integer_value: Option<i32>,
    pub double_value: Option<f64>,
    pub string_value: Option<String>,
    pub point_value: Option<crate::vector3::Vector3>,
    pub formatting: DxfStoredTableContentValueFormatting,
}

/// Mutable edit payload for [`DxfStoredTableContentValue`].
pub type DxfStoredTableContentValueEdit = DxfStoredTableContentValue;

/// Single cell entry inside `TABLECONTENT`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableContentCell {
    pub row: usize,
    pub column: usize,
    pub cell_style_id: Option<i32>,
    pub value: Option<DxfStoredTableContentValue>,
}

/// Raw `AcDbFormattedTableData` / `AcDbTableContent` subclass wrapper.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfStoredTableContentSubclass {
    pub rows: Vec<DxfTableContentBand>,
    pub columns: Vec<DxfTableContentBand>,
    pub cells: Vec<DxfTableContentCell>,
    pub merged_ranges: Vec<DxfTableCellRange>,
}

/// Stored geometry for a single cell in `TABLEGEOMETRY`.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfStoredTableCellGeometry {
    pub width: f64,
    pub height: f64,
    pub flags: i32,
}

/// Stored geometry cell entry in `TABLEGEOMETRY`.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfStoredTableGeometryCell {
    pub row: usize,
    pub column: usize,
    pub geometry: DxfStoredTableCellGeometry,
}
