use crate::dxf_object::DxfObject;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Typed cell value stored inside a DXF `DATATABLE` object.
#[derive(Clone, Debug, PartialEq)]
pub enum DxfDataTableCell {
    Unknown,
    Bool(bool),
    Int32(i32),
    Double(f64),
    String(String),
    Point2D(Vector2),
    Point3D(Vector3),
    ObjectHandle(String),
}

/// Column definition inside a DXF `DATATABLE` object.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfDataTableColumn {
    pub name: String,
    pub column_type: i32,
    pub cells: Vec<DxfDataTableCell>,
}

impl DxfDataTableColumn {
    #[inline] pub fn r#type(&self) -> i32 { self.column_type }
    #[inline] pub fn values(&self) -> &[DxfDataTableCell] { &self.cells }
}

/// Represents a DXF `DATATABLE` object (`netDxf.Objects.DxfDataTable`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfDataTable {
    pub object: DxfObject,
    pub name: String,
    pub version: i16,
    pub row_count: usize,
    pub columns: Vec<DxfDataTableColumn>,
}

impl DxfDataTable {
    pub const MAXIMUM_CELLS: usize = 1_000_000;

    #[inline]
    pub fn stored_version(&self) -> i16 {
        self.version
    }

    pub fn set_columns(&mut self, columns: Vec<DxfDataTableColumn>) {
        self.row_count = columns.first().map_or(0, |c| c.cells.len());
        self.columns = columns;
    }
}

impl Default for DxfDataTable {
    fn default() -> Self {
        Self {
            object: DxfObject::new("DATATABLE"),
            name: String::new(),
            version: 2,
            row_count: 0,
            columns: Vec::new(),
        }
    }
}

/// Cell data type discriminator for [`DxfDataTable`] columns (DXF group 92).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
#[repr(i32)]
pub enum DxfDataCellType {
    Integer = 1,
    Double = 2,
    String = 3,
    Point = 4,
    ObjectId = 5,
    HardOwner = 6,
    SoftOwner = 7,
    HardPointer = 8,
    SoftPointer = 9,
    Boolean = 10,
    Vector = 11,
}

/// Upstream C# `netDxf.Objects.DxfDataColumn` alias.
pub type DxfDataColumn = DxfDataTableColumn;
