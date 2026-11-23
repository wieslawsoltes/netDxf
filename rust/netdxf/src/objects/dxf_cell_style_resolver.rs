use crate::aci_color::AciColor;

/// Cell style formatting attributes (`netDxf.Objects.DxfCellStyleFormat`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct DxfCellStyleFormat {
    pub text_style: Option<String>,
    pub text_height: Option<f64>,
    pub text_color: Option<AciColor>,
    pub background_color: Option<AciColor>,
    pub horizontal_margin: Option<f64>,
    pub vertical_margin: Option<f64>,
    pub alignment: Option<i16>,
    pub format_string: Option<String>,
}

/// Resolves effective table cell formatting by cascading overrides (`netDxf.Objects.DxfCellStyleResolver`).
///
/// Precedence order (highest to lowest): `cell` -> `row` -> `column` -> `table_style`.
pub struct DxfCellStyleResolver;

impl DxfCellStyleResolver {
    pub fn resolve(
        table_style: &DxfCellStyleFormat,
        column: Option<&DxfCellStyleFormat>,
        row: Option<&DxfCellStyleFormat>,
        cell: Option<&DxfCellStyleFormat>,
    ) -> DxfCellStyleFormat {
        let layers: [Option<&DxfCellStyleFormat>; 4] = [cell, row, column, Some(table_style)];
        DxfCellStyleFormat {
            text_style: layers
                .iter()
                .flatten()
                .find_map(|f| f.text_style.clone()),
            text_height: layers.iter().flatten().find_map(|f| f.text_height),
            text_color: layers.iter().flatten().find_map(|f| f.text_color),
            background_color: layers.iter().flatten().find_map(|f| f.background_color),
            horizontal_margin: layers.iter().flatten().find_map(|f| f.horizontal_margin),
            vertical_margin: layers.iter().flatten().find_map(|f| f.vertical_margin),
            alignment: layers.iter().flatten().find_map(|f| f.alignment),
            format_string: layers
                .iter()
                .flatten()
                .find_map(|f| f.format_string.clone()),
        }
    }
}

bitflags::bitflags! {
    /// Bitmask of cell style properties recognized by [`DxfCellStyleResolver`].
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct DxfCellProperty: i32 {
        const NONE = 0;
        const DATA_TYPE = 1;
        const DATA_FORMAT = 2;
        const ROTATION = 4;
        const SCALE = 8;
        const ALIGNMENT = 16;
        const CONTENT_COLOR = 32;
        const TEXT_STYLE = 64;
        const TEXT_HEIGHT = 128;
        const AUTO_SCALE = 256;
        const BACKGROUND_COLOR = 512;
        const MARGIN_LEFT = 1024;
        const MARGIN_TOP = 2048;
        const MARGIN_RIGHT = 4096;
        const MARGIN_BOTTOM = 8192;
        const CONTENT_LAYOUT = 16384;
        const MERGE_ALL = 32768;
        const FLOW_DIRECTION = 65536;
        const HORIZONTAL_SPACING = 131072;
        const VERTICAL_SPACING = 262144;
        const ALL = 524287;
    }
}

/// Cell margin values (left, top, right, bottom).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct DxfCellMargins {
    pub left: f64,
    pub top: f64,
    pub right: f64,
    pub bottom: f64,
}

/// Cell content formatting definition.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfCellContentFormat {
    pub data_type: i32,
    pub unit_type: i32,
    pub format_string: String,
    pub rotation: f64,
    pub scale: f64,
    pub alignment: i32,
    pub text_style: Option<String>,
    pub text_height: Option<f64>,
    pub auto_scale: bool,
}

/// Immutable snapshot of [`DxfCellContentFormat`] values.
pub type DxfCellContentFormatValues = DxfCellContentFormat;

/// Cell grid/border formatting definition.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfCellGridFormat {
    pub border_type: i32,
    pub visible: bool,
    pub lineweight: i16,
    pub double_line_spacing: f64,
    pub color: Option<crate::aci_color::AciColor>,
    pub linetype: Option<String>,
}

/// Cell grid format definition, edit, and values snapshots.
pub type DxfCellGridFormatDefinition = DxfCellGridFormat;
pub type DxfCellGridFormatEdit = DxfCellGridFormat;
pub type DxfCellGridFormatValues = DxfCellGridFormat;

/// Cell style format definition, edit, and values snapshots.
pub type DxfCellStyleFormatDefinition = DxfCellStyleFormat;
pub type DxfCellStyleFormatEdit = DxfCellStyleFormat;
pub type DxfCellStyleFormatValues = DxfCellStyleFormat;

/// Resolved cell style combining base style, row/column, and cell overrides.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfResolvedCellStyle {
    pub format: DxfCellStyleFormat,
    pub margins: DxfCellMargins,
    pub resolved_properties: DxfCellProperty,
}

/// Stored cell style map entry inside a `TABLESTYLE` or `TABLECONTENT`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfStoredCellStyleMapEntry {
    pub id: i32,
    pub style_type: i32,
    pub name: String,
    pub format: DxfCellStyleFormat,
}

/// Definition and edit variants for [`DxfStoredCellStyleMapEntry`].
pub type DxfCellStyleMapEntryDefinition = DxfStoredCellStyleMapEntry;
pub type DxfStoredCellStyleMapEntryEdit = DxfStoredCellStyleMapEntry;
