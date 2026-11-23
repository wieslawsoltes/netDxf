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
    pub version: i16,
    pub horizontal_cell_margin: f64,
    pub vertical_cell_margin: f64,
    pub suppress_title: bool,
    pub suppress_header: bool,
}

impl DxfTableStyleHeader {
    #[inline] pub fn stored_flags(&self) -> i16 { self.flags }
    #[inline] pub fn stored_version(&self) -> i16 { self.version }
    #[inline] pub fn suppress_column_heading(&self) -> bool { self.suppress_header }
}

/// Border style values for a table style row.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableStyleBorderValues {
    pub lineweight: i16,
    pub visible: bool,
    pub color: Option<crate::aci_color::AciColor>,
}

impl DxfTableStyleBorderValues {
    #[inline] pub fn stored_color(&self) -> Option<crate::aci_color::AciColor> { self.color }
    #[inline] pub fn stored_lineweight(&self) -> i16 { self.lineweight }
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

impl DxfTableStyleRowBorders {
    pub const BORDER_COUNT: usize = 6;

    pub fn values(&self) -> [DxfTableStyleBorderValues; 6] {
        [
            self.top.clone(),
            self.horizontal_inside.clone(),
            self.bottom.clone(),
            self.left.clone(),
            self.vertical_inside.clone(),
            self.right.clone(),
        ]
    }

    pub fn with_border(mut self, index: usize, border: DxfTableStyleBorderValues) -> Self {
        match index {
            0 => self.top = border,
            1 => self.horizontal_inside = border,
            2 => self.bottom = border,
            3 => self.left = border,
            4 => self.vertical_inside = border,
            5 => self.right = border,
            _ => {}
        }
        self
    }
}

/// Data type and unit type pair for a table style row.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct DxfTableStyleRowDataTypes {
    pub data_type: i32,
    pub unit_type: i32,
}

impl DxfTableStyleRowDataTypes {
    #[inline] pub fn stored_data_type(&self) -> i32 { self.data_type }
    #[inline] pub fn stored_unit_type(&self) -> i32 { self.unit_type }
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
    #[inline] pub fn background_color_enabled(&self) -> bool { self.fill_enabled }
    #[inline] pub fn cell_alignment(&self) -> i16 { self.alignment }
    #[inline] pub fn stored_fill_color(&self) -> Option<crate::aci_color::AciColor> { self.fill_color }
    #[inline] pub fn stored_text_color(&self) -> Option<crate::aci_color::AciColor> { self.text_color }
    #[inline] pub fn original(&self) -> &Self { self }
    #[inline] pub fn values(&self) -> &Self { self }
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

impl DxfTableCellRange {
    #[inline] pub fn first(&self) -> (usize, usize) { (self.top_row, self.left_column) }
    #[inline] pub fn last(&self) -> (usize, usize) { (self.bottom_row, self.right_column) }
    #[inline] pub fn contains(&self, row: usize, column: usize) -> bool {
        row >= self.top_row && row <= self.bottom_row && column >= self.left_column && column <= self.right_column
    }
}

/// Resolved 2D bounding layout for a single table cell.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableCellLayout {
    pub row: usize,
    pub column: usize,
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
    pub text: String,
    pub style: String,
    pub range: DxfTableCellRange,
}

impl DxfTableCellLayout {
    #[inline] pub fn left(&self) -> f64 { self.x }
    #[inline] pub fn top(&self) -> f64 { self.y }
}

/// Row or column band metadata in `TABLECONTENT`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfTableContentBand {
    pub index: usize,
    pub size: f64,
    pub cell_style_id: Option<i32>,
}

impl DxfTableContentBand {
    #[inline] pub fn format(&self) -> Option<i32> { self.cell_style_id }
    #[inline] pub fn style_id(&self) -> Option<i32> { self.cell_style_id }
}

/// Style reference from a column, row, or cell in `TABLECONTENT`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DxfTableContentStyleReference {
    pub kind: DxfTableContentStyleReferenceKind,
    pub row: usize,
    pub column: usize,
    pub style_id: i32,
}

impl DxfTableContentStyleReference {
    #[inline] pub fn payload_index(&self) -> usize { self.row }
    #[inline] pub fn stored_id(&self) -> i32 { self.style_id }
}

/// Formatting options attached to a [`DxfStoredTableContentValue`].
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfStoredTableContentValueFormatting {
    pub data_type: i32,
    pub unit_type: i32,
    pub format_string: String,
}

impl DxfStoredTableContentValueFormatting {
    pub fn evaluate_formatted_text(&self, text: &str) -> String {
        text.to_string()
    }

    pub fn with_evaluated_value(self) -> Self {
        self
    }
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
    #[inline]
    pub fn original(&self) -> &Self {
        self
    }

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
    pub name: String,
    pub tags: Vec<(i32, String)>,
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
    pub content_width: f64,
    pub content_height: f64,
    pub top_left_distance: crate::vector2::Vector2,
    pub center_distance: crate::vector2::Vector2,
    pub stored_value95: i32,
}

/// Stored geometry cell entry in `TABLEGEOMETRY`.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfStoredTableGeometryCell {
    pub row: usize,
    pub column: usize,
    pub geometry: DxfStoredTableCellGeometry,
    pub width_with_gap: f64,
    pub height_with_gap: f64,
    pub geometry_data_flags: i32,
    pub geometry_reference: i32,
}
