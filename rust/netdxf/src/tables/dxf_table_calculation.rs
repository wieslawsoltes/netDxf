use rustc_hash::FxHashMap;

use crate::error::Result;
use crate::tables::dxf_table_cell_address::DxfTableCellAddress;
use crate::tables::dxf_table_formula::{DxfTableFormula, TableCellValue};

/// Evaluates all formulas in a bounded table grid (`netDxf.Tables.DxfTableCalculation`).
pub struct DxfTableCalculation;

impl DxfTableCalculation {
    /// Evaluates every formula cell in a `rows x cols` grid, returning resolved numeric results.
    pub fn evaluate_grid<F>(
        rows: usize,
        cols: usize,
        mut cell_provider: F,
    ) -> Result<FxHashMap<DxfTableCellAddress, f64>>
    where
        F: FnMut(DxfTableCellAddress) -> TableCellValue,
    {
        let mut results = FxHashMap::default();
        for r in 0..rows {
            for c in 0..cols {
                let addr = DxfTableCellAddress::new(r, c);
                if let TableCellValue::Formula(expr) = cell_provider(addr) {
                    let formula = DxfTableFormula::parse(&expr)?;
                    let val = formula.evaluate(rows, cols, &mut cell_provider)?;
                    results.insert(addr, val);
                }
            }
        }
        Ok(results)
    }
}
