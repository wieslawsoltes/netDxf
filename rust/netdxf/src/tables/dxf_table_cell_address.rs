use std::fmt;
use std::str::FromStr;

use crate::error::{DxfError, Result};

/// Zero-based `(row, column)` table cell address with A1-style parsing and formatting (`netDxf.Tables.DxfTableCellAddress`).
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct DxfTableCellAddress {
    pub row: usize,
    pub column: usize,
}

impl DxfTableCellAddress {
    #[inline]
    pub const fn new(row: usize, column: usize) -> Self {
        Self { row, column }
    }

    /// Parses an A1-style cell address (e.g. `"A1"` -> `(0, 0)`, `"AA10"` -> `(9, 26)`).
    pub fn parse_a1(text: &str) -> Result<Self> {
        let s = text.trim();
        let mut col_end = 0;
        for (idx, b) in s.bytes().enumerate() {
            if b.is_ascii_alphabetic() {
                col_end = idx + 1;
            } else {
                break;
            }
        }
        if col_end == 0 || col_end == s.len() {
            return Err(DxfError::InvalidValue(format!(
                "Invalid A1 cell address '{text}'"
            )));
        }
        let col_str = &s[..col_end];
        let row_str = &s[col_end..];
        if !row_str.bytes().all(|b| b.is_ascii_digit()) {
            return Err(DxfError::InvalidValue(format!(
                "Invalid row in A1 cell address '{text}'"
            )));
        }
        let mut col = 0usize;
        for b in col_str.bytes() {
            let digit = (b.to_ascii_uppercase() - b'A') as usize + 1;
            col = col
                .checked_mul(26)
                .and_then(|v| v.checked_add(digit))
                .ok_or_else(|| DxfError::InvalidValue(format!("Column overflow in '{text}'")))?;
        }
        let row_1based: usize = row_str
            .parse()
            .map_err(|_| DxfError::InvalidValue(format!("Invalid row in '{text}'")))?;
        if row_1based == 0 {
            return Err(DxfError::InvalidValue(format!(
                "Row must be >= 1 in '{text}'"
            )));
        }
        Ok(Self {
            row: row_1based - 1,
            column: col - 1,
        })
    }

    /// Formats the address in A1 notation (e.g. `(0, 0)` -> `"A1"`).
    pub fn to_a1(self) -> String {
        let mut col = self.column + 1;
        let mut letters = Vec::new();
        while col > 0 {
            let rem = (col - 1) % 26;
            letters.push((b'A' + rem as u8) as char);
            col = (col - 1) / 26;
        }
        letters.reverse();
        let col_str: String = letters.into_iter().collect();
        format!("{col_str}{}", self.row + 1)
    }
}

impl fmt::Display for DxfTableCellAddress {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.to_a1())
    }
}

impl FromStr for DxfTableCellAddress {
    type Err = DxfError;
    fn from_str(s: &str) -> Result<Self> {
        Self::parse_a1(s)
    }
}
