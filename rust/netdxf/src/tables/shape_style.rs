use std::fs;
use std::path::Path;

use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};

/// Represents a DXF shape style (`STYLE` with shape flag bit 1).
#[derive(Clone, Debug, PartialEq)]
pub struct ShapeStyle {
    pub table: TableObjectData,
    pub file: String,
    pub size: f64,
    pub last_height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub flags: i16,
}

impl ShapeStyle {
    pub const DEFAULT_SHAPE_FILE: &'static str = "ltypeshp.shx";

    pub fn new(name: impl Into<String>, file: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::TEXT_STYLE, false)?,
            file: file.into(),
            size: 0.0,
            last_height: 0.0,
            width_factor: 1.0,
            oblique_angle: 0.0,
            flags: 1,
        })
    }

    /// Reads shape names from an AutoCAD `.shp` ASCII shape definition file (`*number,bytes,NAME`).
    pub fn names_from_file(path: impl AsRef<Path>) -> Result<Vec<String>> {
        let text = fs::read_to_string(path)?;
        let mut names = Vec::new();
        for line in text.lines() {
            let trimmed = line.trim();
            if let Some(rest) = trimmed.strip_prefix('*') {
                let parts: Vec<&str> = rest.splitn(3, ',').collect();
                if parts.len() == 3 {
                    let name = parts[2].trim();
                    if !name.is_empty() {
                        names.push(name.to_string());
                    }
                }
            }
        }
        Ok(names)
    }

    /// Reads shape names from the `.shp` file associated with this `ShapeStyle`.
    pub fn names_from_shape_style(&self) -> Result<Vec<String>> {
        let shp = Path::new(&self.file).with_extension("shp");
        Self::names_from_file(shp)
    }

    /// Checks whether the `.shp` file for this style defines a shape named `shape_name`.
    pub fn contains_shape_name(&self, shape_name: &str) -> bool {
        self.names_from_shape_style()
            .map(|names| names.iter().any(|n| n.eq_ignore_ascii_case(shape_name)))
            .unwrap_or(false)
    }

    /// Looks up the first shape name defined in this shape style's `.shp` file.
    pub fn shape_name(&self, shape_number: i16) -> Option<String> {
        let shp = Path::new(&self.file).with_extension("shp");
        let text = fs::read_to_string(shp).ok()?;
        let target = shape_number.to_string();
        for line in text.lines() {
            let trimmed = line.trim();
            if let Some(rest) = trimmed.strip_prefix('*') {
                let parts: Vec<&str> = rest.splitn(3, ',').collect();
                if parts.len() == 3 && parts[0].trim() == target {
                    return Some(parts[2].trim().to_string());
                }
            }
        }
        None
    }
}

impl TableObject for ShapeStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
