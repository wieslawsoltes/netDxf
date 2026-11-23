use crate::aci_color::AciColor;
use crate::error::{DxfError, Result};
use crate::lineweight::Lineweight;
use crate::tables::dimension_style::{
    DimensionStyleFitOptions, DimensionStyleFitTextMove, DimensionStyleTextDirection,
    DimensionStyleTextHorizontalPlacement, DimensionStyleTextVerticalPlacement,
    DimensionStyleTolerancesDisplayMethod, DimensionStyleTolerancesVerticalPlacement,
};
use crate::tables::DimensionStyleOverrideType;
use crate::units::{AngleUnitType, FractionFormatType, LinearUnitType};

/// Typed value carried by a [`DimensionStyleOverride`].
#[derive(Debug, Clone, PartialEq)]
pub enum DimensionStyleOverrideValue {
    Bool(bool),
    Int16(i16),
    Float64(f64),
    Char(char),
    String(String),
    OptionalString(Option<String>),
    Color(AciColor),
    OptionalColor(Option<AciColor>),
    Lineweight(Lineweight),
    TextVerticalPlacement(DimensionStyleTextVerticalPlacement),
    TextHorizontalPlacement(DimensionStyleTextHorizontalPlacement),
    TextDirection(DimensionStyleTextDirection),
    FitOptions(DimensionStyleFitOptions),
    FitTextMove(DimensionStyleFitTextMove),
    LinearUnitType(LinearUnitType),
    AngleUnitType(AngleUnitType),
    FractionFormatType(FractionFormatType),
    TolerancesDisplayMethod(DimensionStyleTolerancesDisplayMethod),
    TolerancesVerticalPlacement(DimensionStyleTolerancesVerticalPlacement),
}

/// Represents a dimension style value that overrides a property of the style associated with a dimension.
#[derive(Debug, Clone, PartialEq)]
pub struct DimensionStyleOverride {
    pub override_type: DimensionStyleOverrideType,
    pub value: DimensionStyleOverrideValue,
}

impl DimensionStyleOverride {
    #[inline]
    pub fn r#type(&self) -> DimensionStyleOverrideType {
        self.override_type
    }

    /// Creates and validates a new [`DimensionStyleOverride`].
    pub fn new(
        override_type: DimensionStyleOverrideType,
        value: DimensionStyleOverrideValue,
    ) -> Result<Self> {
        use DimensionStyleOverrideType as T;
        use DimensionStyleOverrideValue as V;

        let valid = match (&override_type, &value) {
            (T::DimLineColor | T::ExtLineColor | T::TextColor, V::Color(_)) => true,
            (T::TextFillColor, V::OptionalColor(_) | V::Color(_)) => true,
            (T::DimLineLineweight | T::ExtLineLineweight, V::Lineweight(_)) => true,
            (
                T::DimLineLinetype
                | T::ExtLine1Linetype
                | T::ExtLine2Linetype
                | T::TextStyle
                | T::DimPrefix
                | T::DimSuffix
                | T::AltUnitsPrefix
                | T::AltUnitsSuffix,
                V::String(_),
            ) => true,
            (
                T::DimArrow1 | T::DimArrow2 | T::LeaderArrow,
                V::OptionalString(_) | V::String(_),
            ) => true,
            (
                T::DimLine1Off
                | T::DimLine2Off
                | T::ExtLine1Off
                | T::ExtLine2Off
                | T::ExtLineFixed
                | T::TextInsideAlign
                | T::TextOutsideAlign
                | T::FitDimLineForce
                | T::FitDimLineInside
                | T::FitTextInside
                | T::SuppressLinearLeadingZeros
                | T::SuppressLinearTrailingZeros
                | T::SuppressAngularLeadingZeros
                | T::SuppressAngularTrailingZeros
                | T::SuppressZeroFeet
                | T::SuppressZeroInches
                | T::AltUnitsEnabled
                | T::AltUnitsStackedUnits
                | T::AltUnitsSuppressLinearLeadingZeros
                | T::AltUnitsSuppressLinearTrailingZeros
                | T::AltUnitsSuppressZeroFeet
                | T::AltUnitsSuppressZeroInches
                | T::TolerancesSuppressLinearLeadingZeros
                | T::TolerancesSuppressLinearTrailingZeros
                | T::TolerancesSuppressZeroFeet
                | T::TolerancesSuppressZeroInches
                | T::TolerancesAltSuppressLinearLeadingZeros
                | T::TolerancesAltSuppressLinearTrailingZeros
                | T::TolerancesAltSuppressZeroFeet
                | T::TolerancesAltSuppressZeroInches
                | T::UserPositionedText,
                V::Bool(_),
            ) => true,
            (
                T::DimLineExtend
                | T::ExtLineOffset
                | T::ExtLineExtend
                | T::ExtLineFixedLength
                | T::ArrowSize
                | T::DimRoundoff
                | T::AltUnitsRoundoff
                | T::TickSize,
                V::Float64(v),
            ) => v.is_finite() && *v >= 0.0,
            (
                T::TextHeight
                | T::TextFractionHeightScale
                | T::DimScaleOverall
                | T::AltUnitsMultiplier,
                V::Float64(v),
            ) => v.is_finite() && *v > 0.0,
            (
                T::CenterMarkSize
                | T::TextOffset
                | T::DimScaleLinear
                | T::TolerancesUpperLimit
                | T::TolerancesLowerLimit
                | T::TextVerticalPosition,
                V::Float64(v),
            ) => v.is_finite(),
            (
                T::AngularPrecision
                | T::LengthPrecision
                | T::AltUnitsLengthPrecision
                | T::TolerancesPrecision
                | T::TolerancesAlternatePrecision,
                V::Int16(v),
            ) => *v >= 0,
            (T::DecimalSeparator, V::Char(_)) => true,
            (T::TextVerticalPlacement, V::TextVerticalPlacement(_)) => true,
            (T::TextHorizontalPlacement, V::TextHorizontalPlacement(_)) => true,
            (T::TextDirection, V::TextDirection(_)) => true,
            (T::FitOptions, V::FitOptions(_)) => true,
            (T::FitTextMove, V::FitTextMove(_)) => true,
            (T::DimLengthUnits | T::AltUnitsLengthUnits, V::LinearUnitType(_)) => true,
            (T::DimAngularUnits, V::AngleUnitType(_)) => true,
            (T::FractionalType, V::FractionFormatType(_)) => true,
            (T::TolerancesDisplayMethod, V::TolerancesDisplayMethod(_)) => true,
            (T::TolerancesVerticalPlacement, V::TolerancesVerticalPlacement(_)) => true,
            _ => false,
        };

        if !valid {
            return Err(DxfError::InvalidValue(format!(
                "Invalid value for DimensionStyleOverrideType::{override_type:?}."
            )));
        }

        Ok(Self {
            override_type,
            value,
        })
    }
}
