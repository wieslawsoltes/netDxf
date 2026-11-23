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
