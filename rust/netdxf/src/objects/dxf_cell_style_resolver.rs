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
    pub borders: Vec<DxfCellGridFormat>,
    pub margins: Option<DxfCellMargins>,
    pub content: Option<DxfCellContentFormat>,
    pub stored_type: i32,
    pub stored_data_flags: i32,
    pub stored_margin_flags: i32,
}

impl DxfCellStyleFormat {
    pub fn edit(&self) -> Self {
        self.clone()
    }

    pub fn from_format(fmt: &Self) -> Self {
        fmt.clone()
    }

    pub fn with_borders(mut self, borders: Vec<DxfCellGridFormat>) -> Self {
        self.borders = borders;
        self
    }

    pub fn with_margins(mut self, margins: DxfCellMargins) -> Self {
        self.margins = Some(margins);
        self
    }

    pub fn with_content(mut self, content: DxfCellContentFormat) -> Self {
        self.content = Some(content);
        self
    }

    pub fn with_text_style(mut self, text_style: impl Into<String>) -> Self {
        self.text_style = Some(text_style.into());
        self
    }

    pub fn with_values(self, values: Self) -> Self {
        values
    }

    #[inline]
    pub fn original(&self) -> &Self {
        self
    }

    #[inline]
    pub fn values(&self) -> &Self {
        self
    }

    #[inline]
    pub fn changes_text_style(&self) -> bool {
        self.text_style.is_some()
    }

    #[inline] pub fn stored_background_color(&self) -> Option<AciColor> { self.background_color }
    #[inline] pub fn stored_content_layout(&self) -> i32 { self.stored_data_flags }
    #[inline] pub fn stored_merge_flags(&self) -> i32 { self.stored_margin_flags }
    #[inline] pub fn stored_property_overrides(&self) -> i32 { self.stored_data_flags }

    pub fn remap_resources<F>(&mut self, mut map_text_style: F)
    where
        F: FnMut(&str) -> String,
    {
        if let Some(ts) = self.text_style.as_deref() {
            self.text_style = Some(map_text_style(ts));
        }
    }
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
            ..Default::default()
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

impl DxfCellMargins {
    #[inline] pub fn bottom_margin(&self) -> f64 { self.bottom }
    #[inline] pub fn right_margin(&self) -> f64 { self.right }
    #[inline] pub fn horizontal_margin(&self) -> f64 { self.left }
    #[inline] pub fn vertical_margin(&self) -> f64 { self.top }
    #[inline] pub fn horizontal_spacing(&self) -> f64 { self.left + self.right }
    #[inline] pub fn vertical_spacing(&self) -> f64 { self.top + self.bottom }
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
    pub property_flags: i32,
    pub property_overrides: i32,
    pub color: Option<AciColor>,
}

impl DxfCellContentFormat {
    #[inline] pub fn block_scale(&self) -> f64 { self.scale }
    #[inline] pub fn stored_alignment(&self) -> i32 { self.alignment }
    #[inline] pub fn stored_color(&self) -> Option<AciColor> { self.color }
    #[inline] pub fn stored_data_type(&self) -> i32 { self.data_type }
    #[inline] pub fn stored_property_flags(&self) -> i32 { self.property_flags }
    #[inline] pub fn stored_property_overrides(&self) -> i32 { self.property_overrides }
    #[inline] pub fn stored_unit_type(&self) -> i32 { self.unit_type }
    #[inline] pub fn stored_text_style_handle(&self) -> Option<&str> { self.text_style.as_deref() }
    #[inline] pub fn values(&self) -> &Self { self }
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
    pub property_overrides: i32,
}

impl DxfCellGridFormat {
    #[inline] pub fn edit(&self) -> Self { self.clone() }
    #[inline] pub fn stored_index_mask(&self) -> i32 { self.border_type }
    #[inline] pub fn stored_linetype_handle(&self) -> Option<&str> { self.linetype.as_deref() }
    #[inline] pub fn changes_linetype(&self) -> bool { self.linetype.is_some() }
    #[inline] pub fn original(&self) -> &Self { self }
    #[inline] pub fn values(&self) -> &Self { self }
    pub fn with_linetype(mut self, linetype: impl Into<String>) -> Self {
        self.linetype = Some(linetype.into());
        self
    }
    pub fn with_values(mut self, values: &Self) -> Self {
        self.border_type = values.border_type;
        self.visible = values.visible;
        self.lineweight = values.lineweight;
        self.double_line_spacing = values.double_line_spacing;
        self.color = values.color;
        self
    }
    #[inline] pub fn stored_border_type(&self) -> i32 { self.border_type }
    #[inline] pub fn stored_color(&self) -> Option<AciColor> { self.color }
    #[inline] pub fn stored_lineweight(&self) -> i16 { self.lineweight }
    #[inline] pub fn stored_property_overrides(&self) -> i32 { self.property_overrides }
    #[inline] pub fn stored_visibility(&self) -> bool { self.visible }
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

impl DxfResolvedCellStyle {
    #[inline]
    pub fn property_sources(&self) -> DxfCellProperty {
        self.resolved_properties
    }
}

/// Stored cell style map entry inside a `TABLESTYLE` or `TABLECONTENT`.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct DxfStoredCellStyleMapEntry {
    pub id: i32,
    pub style_type: i32,
    pub name: String,
    pub format: DxfCellStyleFormat,
}

impl DxfStoredCellStyleMapEntry {
    pub fn from_entry(entry: &Self) -> Self {
        entry.clone()
    }

    #[inline]
    pub fn format_payload(&self) -> &DxfCellStyleFormat {
        &self.format
    }

    #[inline]
    pub fn stored_type(&self) -> i32 {
        self.style_type
    }

    #[inline]
    pub fn original(&self) -> &Self {
        self
    }

    pub fn with_format(mut self, format: DxfCellStyleFormat) -> Self {
        self.format = format;
        self
    }

    pub fn with_name(mut self, name: impl Into<String>) -> Self {
        self.name = name.into();
        self
    }
}

/// Definition and edit variants for [`DxfStoredCellStyleMapEntry`].
pub type DxfCellStyleMapEntryDefinition = DxfStoredCellStyleMapEntry;
pub type DxfStoredCellStyleMapEntryEdit = DxfStoredCellStyleMapEntry;
