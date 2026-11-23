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
    pub header: DxfTableStyleHeader,
    pub rows: Vec<DxfTableStyleRow>,
    pub cell_style_map: Option<String>,
    pub stored_cell_style_map: Option<String>,
    pub source_version: Option<crate::header::DxfVersion>,
    pub references: Vec<String>,
    pub tags: Vec<(i32, String)>,
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
            header: DxfTableStyleHeader::default(),
            rows: Vec::new(),
            cell_style_map: None,
            stored_cell_style_map: None,
            source_version: None,
            references: Vec::new(),
            tags: Vec::new(),
        }
    }

    pub fn replace_style(&mut self, index: usize, row: DxfTableStyleRow) -> bool {
        if let Some(slot) = self.rows.get_mut(index) {
            *slot = row;
            true
        } else {
            false
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
    pub tags: Vec<(i32, String)>,
}

impl DxfTableStyleRow {
    #[inline] pub fn stored_text_style_name(&self) -> &str { &self.text_style }
    pub fn with_borders(mut self, borders: DxfTableStyleRowBorders) -> Self {
        self.borders = borders;
        self
    }
    pub fn with_data_types(mut self, data_types: DxfTableStyleRowDataTypes) -> Self {
        self.data_types = data_types;
        self
    }
    pub fn with_text_style(mut self, text_style: impl Into<String>) -> Self {
        self.text_style = text_style.into();
        self
    }
    pub fn with_values(self, values: Self) -> Self {
        values
    }
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
    pub formatted_text: String,
    pub payload_index: usize,
    pub stored_format_flags: i32,
    pub stored_unit_type: i32,
    pub tags: Vec<(i32, String)>,
}

impl DxfStoredTableContentValue {
    pub fn format_string(&self) -> &str {
        &self.formatting.format_string
    }

    pub fn value(&self) -> Option<String> {
        if let Some(s) = &self.string_value {
            Some(s.clone())
        } else if let Some(d) = self.double_value {
            Some(d.to_string())
        } else {
            self.integer_value.map(|i| i.to_string())
        }
    }

    pub fn with_value(mut self, text: impl Into<String>) -> Self {
        let s = text.into();
        self.kind = DxfStoredTableContentValueKind::String;
        self.string_value = Some(s.clone());
        self.formatted_text = s;
        self
    }
}

/// Mutable edit payload for [`DxfStoredTableContentValue`].
pub type DxfStoredTableContentValueEdit = DxfStoredTableContentValue;

/// Single cell entry inside `TABLECONTENT`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableContentCell {
    pub row: usize,
    pub column: usize,
    pub cell_style_id: Option<i32>,
    pub declared_content_count: usize,
    pub format: Option<String>,
    pub value: Option<DxfStoredTableContentValue>,
    pub contents: Vec<DxfStoredTableContentValue>,
}

impl DxfTableContentCell {
    pub fn address(&self) -> (usize, usize) {
        (self.row, self.column)
    }

    pub fn style_id(&self) -> Option<i32> {
        self.cell_style_id
    }

    pub fn has_complete_scalar_content(&self) -> bool {
        self.value.is_some() || !self.contents.is_empty()
    }
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
