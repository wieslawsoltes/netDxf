use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::lineweight::Lineweight;
use crate::tables::linetype::Linetype;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::tables::text_style::TextStyle;
use crate::units::{AngleUnitType, FractionFormatType, LinearUnitType};

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionStyleTextDirection {
    #[default]
    LeftToRight = 0,
    RightToLeft = 1,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionStyleTextVerticalPlacement {
    #[default]
    Centered = 0,
    Above = 1,
    Outside = 2,
    JIS = 3,
    Below = 4,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionStyleTextHorizontalPlacement {
    #[default]
    Centered = 0,
    AtExtLine1 = 1,
    AtExtLine2 = 2,
    OverExtLine1 = 3,
    OverExtLine2 = 4,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionStyleFitOptions {
    TextAndArrows = 0,
    ArrowsFirst = 1,
    TextFirst = 2,
    #[default]
    BestFit = 3,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionStyleFitTextMove {
    #[default]
    BesideDimLine = 0,
    OverDimLineWithLeader = 1,
    OverDimLineWithoutLeader = 2,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum DimensionStyleTolerancesDisplayMethod {
    #[default]
    None,
    Symmetrical,
    Deviation,
    Limits,
    Basic,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DimensionStyleTolerancesVerticalPlacement {
    Bottom = 0,
    #[default]
    Middle = 1,
    Top = 2,
}

#[derive(Clone, Debug, PartialEq)]
pub struct DimensionStyleTolerances {
    pub display_method: DimensionStyleTolerancesDisplayMethod,
    pub precision: i16,
    pub upper_limit: f64,
    pub lower_limit: f64,
    pub vertical_placement: DimensionStyleTolerancesVerticalPlacement,
    pub suppress_linear_leading_zeros: bool,
    pub suppress_linear_trailing_zeros: bool,
    pub suppress_zero_feet: bool,
    pub suppress_zero_inches: bool,
    pub alternate_precision: i16,
    pub alternate_suppress_linear_leading_zeros: bool,
    pub alternate_suppress_linear_trailing_zeros: bool,
    pub alternate_suppress_zero_feet: bool,
    pub alternate_suppress_zero_inches: bool,
}

impl Default for DimensionStyleTolerances {
    fn default() -> Self {
        Self {
            display_method: DimensionStyleTolerancesDisplayMethod::None,
            precision: 4,
            upper_limit: 0.0,
            lower_limit: 0.0,
            vertical_placement: DimensionStyleTolerancesVerticalPlacement::Middle,
            suppress_linear_leading_zeros: false,
            suppress_linear_trailing_zeros: false,
            suppress_zero_feet: true,
            suppress_zero_inches: true,
            alternate_precision: 2,
            alternate_suppress_linear_leading_zeros: false,
            alternate_suppress_linear_trailing_zeros: false,
            alternate_suppress_zero_feet: true,
            alternate_suppress_zero_inches: true,
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct DimensionStyleAlternateUnits {
    pub enabled: bool,
    pub length_units: LinearUnitType,
    pub stacked_units: bool,
    pub length_precision: i16,
    pub multiplier: f64,
    pub roundoff: f64,
    pub prefix: String,
    pub suffix: String,
    pub suppress_linear_leading_zeros: bool,
    pub suppress_linear_trailing_zeros: bool,
    pub suppress_zero_feet: bool,
    pub suppress_zero_inches: bool,
    pub sub_unit_factor: f64,
    pub sub_unit_suffix: String,
}

impl Default for DimensionStyleAlternateUnits {
    fn default() -> Self {
        Self {
            enabled: false,
            length_units: LinearUnitType::Decimal,
            stacked_units: false,
            length_precision: 2,
            multiplier: 25.4,
            roundoff: 0.0,
            prefix: String::new(),
            suffix: String::new(),
            suppress_linear_leading_zeros: false,
            suppress_linear_trailing_zeros: false,
            suppress_zero_feet: true,
            suppress_zero_inches: true,
            sub_unit_factor: 100.0,
            sub_unit_suffix: String::new(),
        }
    }
}

/// Represents a DXF dimension style (`DIMSTYLE`).
#[derive(Clone, Debug, PartialEq)]
pub struct DimensionStyle {
    pub table: TableObjectData,
    pub dim_line_color: AciColor,
    pub dim_line_linetype: String,
    pub dim_line_lineweight: Lineweight,
    pub dim_line1_off: bool,
    pub dim_line2_off: bool,
    pub dim_line_extend: f64,
    pub dim_baseline_spacing: f64,
    pub ext_line_color: AciColor,
    pub ext_line1_linetype: String,
    pub ext_line2_linetype: String,
    pub ext_line_lineweight: Lineweight,
    pub ext_line1_off: bool,
    pub ext_line2_off: bool,
    pub ext_line_offset: f64,
    pub ext_line_extend: f64,
    pub ext_line_fixed: bool,
    pub ext_line_fixed_length: f64,
    pub dim_arrow1: Option<String>,
    pub dim_arrow2: Option<String>,
    pub leader_arrow: Option<String>,
    pub arrow_size: f64,
    pub center_mark_size: f64,
    pub text_style: String,
    pub text_color: AciColor,
    pub text_fill_color: Option<AciColor>,
    pub text_height: f64,
    pub text_horizontal_placement: DimensionStyleTextHorizontalPlacement,
    pub text_vertical_placement: DimensionStyleTextVerticalPlacement,
    pub text_offset: f64,
    pub text_inside_horizontal: bool,
    pub text_outside_horizontal: bool,
    pub text_direction: DimensionStyleTextDirection,
    pub text_fraction_height_scale: f64,
    pub dim_scale_overall: f64,
    pub fit_dim_line_force: bool,
    pub fit_dim_line_inside: bool,
    pub fit_options: DimensionStyleFitOptions,
    pub fit_text_inside: bool,
    pub fit_text_move: DimensionStyleFitTextMove,
    pub angular_precision: i16,
    pub length_precision: i16,
    pub dim_prefix: String,
    pub dim_suffix: String,
    pub decimal_separator: char,
    pub dim_scale_linear: f64,
    pub dim_length_units: LinearUnitType,
    pub dim_angular_units: AngleUnitType,
    pub fractional_type: FractionFormatType,
    pub suppress_linear_leading_zeros: bool,
    pub suppress_linear_trailing_zeros: bool,
    pub suppress_angular_leading_zeros: bool,
    pub suppress_angular_trailing_zeros: bool,
    pub suppress_zero_feet: bool,
    pub suppress_zero_inches: bool,
    pub dim_roundoff: f64,
    pub alternate_units: DimensionStyleAlternateUnits,
    pub tolerances: DimensionStyleTolerances,
}

impl DimensionStyle {
    pub const DEFAULT_NAME: &'static str = "Standard";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        let mut style = Self::default_style();
        style.table = TableObjectData::new(name, DxfObjectCode::DIM_STYLE, true)?;
        Ok(style)
    }

    pub fn default_style() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::DIM_STYLE),
            dim_line_color: AciColor::BY_BLOCK,
            dim_line_linetype: Linetype::BY_BLOCK_NAME.to_string(),
            dim_line_lineweight: Lineweight::ByBlock,
            dim_line1_off: false,
            dim_line2_off: false,
            dim_line_extend: 0.0,
            dim_baseline_spacing: 0.38,
            ext_line_color: AciColor::BY_BLOCK,
            ext_line1_linetype: Linetype::BY_BLOCK_NAME.to_string(),
            ext_line2_linetype: Linetype::BY_BLOCK_NAME.to_string(),
            ext_line_lineweight: Lineweight::ByBlock,
            ext_line1_off: false,
            ext_line2_off: false,
            ext_line_offset: 0.0625,
            ext_line_extend: 0.18,
            ext_line_fixed: false,
            ext_line_fixed_length: 1.0,
            dim_arrow1: None,
            dim_arrow2: None,
            leader_arrow: None,
            arrow_size: 0.18,
            center_mark_size: 0.09,
            text_style: TextStyle::DEFAULT_NAME.to_string(),
            text_color: AciColor::BY_BLOCK,
            text_fill_color: None,
            text_height: 0.18,
            text_horizontal_placement: DimensionStyleTextHorizontalPlacement::Centered,
            text_vertical_placement: DimensionStyleTextVerticalPlacement::Centered,
            text_offset: 0.09,
            text_inside_horizontal: true,
            text_outside_horizontal: true,
            text_direction: DimensionStyleTextDirection::LeftToRight,
            text_fraction_height_scale: 1.0,
            dim_scale_overall: 1.0,
            fit_dim_line_force: false,
            fit_dim_line_inside: false,
            fit_options: DimensionStyleFitOptions::BestFit,
            fit_text_inside: false,
            fit_text_move: DimensionStyleFitTextMove::BesideDimLine,
            angular_precision: 0,
            length_precision: 4,
            dim_prefix: String::new(),
            dim_suffix: String::new(),
            decimal_separator: '.',
            dim_scale_linear: 1.0,
            dim_length_units: LinearUnitType::Decimal,
            dim_angular_units: AngleUnitType::DecimalDegrees,
            fractional_type: FractionFormatType::Horizontal,
            suppress_linear_leading_zeros: false,
            suppress_linear_trailing_zeros: false,
            suppress_angular_leading_zeros: false,
            suppress_angular_trailing_zeros: false,
            suppress_zero_feet: true,
            suppress_zero_inches: true,
            dim_roundoff: 0.0,
            alternate_units: DimensionStyleAlternateUnits::default(),
            tolerances: DimensionStyleTolerances::default(),
        }
    }

    pub fn iso25() -> Self {
        let mut s = Self::default_style();
        s.table = TableObjectData::new("ISO-25", DxfObjectCode::DIM_STYLE, false).unwrap();
        s.dim_baseline_spacing = 3.75;
        s.ext_line_extend = 1.25;
        s.ext_line_offset = 0.625;
        s.arrow_size = 2.5;
        s.center_mark_size = 2.5;
        s.text_height = 2.5;
        s.text_vertical_placement = DimensionStyleTextVerticalPlacement::Above;
        s.text_offset = 0.625;
        s.text_inside_horizontal = false;
        s.text_outside_horizontal = false;
        s.fit_dim_line_force = true;
        s.length_precision = 2;
        s.decimal_separator = ',';
        s.suppress_linear_trailing_zeros = true;
        s.suppress_angular_trailing_zeros = true;
        s
    }
}

impl TableObject for DimensionStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
