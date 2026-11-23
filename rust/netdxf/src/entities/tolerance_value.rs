use crate::entities::ToleranceMaterialCondition;

/// Represents a tolerance value indicating the permissible deviation from a perfect form.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct ToleranceValue {
    pub show_diameter_symbol: bool,
    pub value: String,
    pub material_condition: ToleranceMaterialCondition,
}

impl ToleranceValue {
    pub fn new(
        show_diameter_symbol: bool,
        value: impl Into<String>,
        material_condition: ToleranceMaterialCondition,
    ) -> Self {
        Self {
            show_diameter_symbol,
            value: value.into(),
            material_condition,
        }
    }
}
