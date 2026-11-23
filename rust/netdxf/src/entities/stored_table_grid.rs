/// Stored row and column geometry grid for an `ACAD_TABLE` (`StoredTable`).
#[derive(Debug, Clone, PartialEq, Default)]
pub struct StoredTableGrid {
    pub row_heights: Vec<f64>,
    pub column_widths: Vec<f64>,
}

impl StoredTableGrid {
    pub fn new(row_heights: Vec<f64>, column_widths: Vec<f64>) -> Self {
        Self {
            row_heights,
            column_widths,
        }
    }

    pub fn row_count(&self) -> usize {
        self.row_heights.len()
    }

    pub fn column_count(&self) -> usize {
        self.column_widths.len()
    }
}
