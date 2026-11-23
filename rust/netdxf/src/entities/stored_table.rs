use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::error::Result;
use crate::tables::{DxfTableCalculation, DxfTableCellAddress, TableCellValue};
use crate::vector3::Vector3;

/// A single cell in an `ACAD_TABLE` (`StoredTable`).
#[derive(Clone, Debug, PartialEq)]
pub struct StoredTableCell {
    pub row: usize,
    pub column: usize,
    pub cell_type: i16,
    pub stored_flags: i32,
    pub text: String,
    pub formula: Option<String>,
    pub field_handle: Option<String>,
    pub value: TableCellValue,
    pub block_name: Option<String>,
    pub tags: Vec<(i32, String)>,
}

impl StoredTableCell {
    pub fn text(row: usize, column: usize, text: impl Into<String>) -> Self {
        let s = text.into();
        let value = if s.starts_with('=') {
            TableCellValue::Formula(s.clone())
        } else if s.is_empty() {
            TableCellValue::Empty
        } else {
            TableCellValue::Text(s.clone())
        };
        let formula = if s.starts_with('=') { Some(s.clone()) } else { None };
        Self {
            row,
            column,
            cell_type: 1,
            stored_flags: 0,
            text: s,
            formula,
            field_handle: None,
            value,
            block_name: None,
            tags: Vec::new(),
        }
    }

    pub fn number(row: usize, column: usize, value: f64) -> Self {
        Self {
            row,
            column,
            cell_type: 1,
            stored_flags: 0,
            text: format!("{value}"),
            formula: None,
            field_handle: None,
            value: TableCellValue::Float(value),
            block_name: None,
            tags: Vec::new(),
        }
    }

    pub fn stored_type(&self) -> i16 {
        self.cell_type
    }

    pub fn value_type(&self) -> &'static str {
        match &self.value {
            TableCellValue::Empty => "Empty",
            TableCellValue::Float(_) => "Double",
            TableCellValue::Int(_) => "Long",
            TableCellValue::Text(_) => "String",
            TableCellValue::Formula(_) => "Formula",
        }
    }

    pub fn has_literal_value(&self) -> bool {
        !matches!(self.value, TableCellValue::Empty)
    }

    pub fn literal_value(&self) -> &str {
        &self.text
    }

    pub fn has_field_reference(&self) -> bool {
        self.field_handle.is_some()
    }
}

/// Represents a DXF `ACAD_TABLE` entity (`netDxf.Entities.StoredTable`).
#[derive(Clone, Debug, PartialEq)]
pub struct StoredTable {
    pub common: EntityCommon,
    pub insertion_point: Vector3,
    pub horizontal_direction: Vector3,
    pub table_style: String,
    pub block_name: String,
    pub row_count: usize,
    pub column_count: usize,
    pub row_heights: Vec<f64>,
    pub column_widths: Vec<f64>,
    pub cells: Vec<StoredTableCell>,
    pub backing_content: Option<String>,
    pub stored_backing_content: Option<String>,
    pub source_version: Option<crate::header::DxfVersion>,
    pub payload: Vec<(i32, String)>,
}

impl StoredTable {
    pub fn new(row_count: usize, column_count: usize) -> Self {
        let mut cells = Vec::with_capacity(row_count * column_count);
        for r in 0..row_count {
            for c in 0..column_count {
                cells.push(StoredTableCell::text(r, c, ""));
            }
        }
        Self {
            common: EntityCommon::new(EntityType::Table, DxfObjectCode::ACAD_TABLE),
            insertion_point: Vector3::ZERO,
            horizontal_direction: Vector3::UNIT_X,
            table_style: "Standard".to_string(),
            block_name: String::new(),
            row_count,
            column_count,
            row_heights: vec![10.0; row_count],
            column_widths: vec![30.0; column_count],
            cells,
            backing_content: None,
            stored_backing_content: None,
            source_version: None,
            payload: Vec::new(),
        }
    }

    pub fn references(&self) -> Vec<&str> {
        let mut out = Vec::new();
        if !self.block_name.is_empty() {
            out.push(self.block_name.as_str());
        }
        if let Some(bc) = self.backing_content.as_deref() {
            out.push(bc);
        }
        for cell in &self.cells {
            if let Some(fh) = cell.field_handle.as_deref() {
                out.push(fh);
            }
        }
        out
    }

    pub fn get_cell(&self, row: usize, column: usize) -> Option<&StoredTableCell> {
        if row < self.row_count && column < self.column_count {
            self.cells.get(row * self.column_count + column)
        } else {
            None
        }
    }

    pub fn get_cell_mut(&mut self, row: usize, column: usize) -> Option<&mut StoredTableCell> {
        if row < self.row_count && column < self.column_count {
            self.cells.get_mut(row * self.column_count + column)
        } else {
            None
        }
    }

    pub fn set_cell_text(&mut self, row: usize, column: usize, text: impl Into<String>) {
        if let Some(cell) = self.get_cell_mut(row, column) {
            *cell = StoredTableCell::text(row, column, text);
        }
    }

    pub fn set_cell_number(&mut self, row: usize, column: usize, value: f64) {
        if let Some(cell) = self.get_cell_mut(row, column) {
            *cell = StoredTableCell::number(row, column, value);
        }
    }

    #[inline] pub fn position(&self) -> Vector3 { self.insertion_point }
    #[inline] pub fn normal(&self) -> Vector3 { self.common.normal }
    #[inline] pub fn display_block(&self) -> &str { &self.block_name }
    pub fn replace_display_block(&mut self, block_name: impl Into<String>) {
        self.block_name = block_name.into();
    }
    #[inline] pub fn grid(&self) -> (usize, usize) { (self.row_count, self.column_count) }
    #[inline] pub fn backing_literal_values_agree(&self) -> bool {
        self.cells.iter().all(|c| match &c.value {
            TableCellValue::Text(s) => s == &c.text,
            TableCellValue::Empty => c.text.is_empty(),
            _ => true,
        })
    }

    /// Evaluates all formula cells in this table and updates their display text.
    pub fn recalculate(&mut self) -> Result<()> {
        let rows = self.row_count;
        let cols = self.column_count;
        let snapshot = self.cells.clone();
        let results = DxfTableCalculation::evaluate_grid(rows, cols, |addr: DxfTableCellAddress| {
            snapshot
                .get(addr.row * cols + addr.column)
                .map(|c| c.value.clone())
                .unwrap_or(TableCellValue::Empty)
        })?;
        for (addr, val) in results {
            if let Some(cell) = self.get_cell_mut(addr.row, addr.column) {
                cell.text = format!("{val}");
            }
        }
        Ok(())
    }
}
