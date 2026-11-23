use crate::entities::ToleranceMaterialCondition;

/// Represents a datum reference: a theoretically exact point, axis, or plane.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DatumReferenceValue {
    pub value: String,
    pub material_condition: ToleranceMaterialCondition,
}

impl DatumReferenceValue {
    pub fn new(value: impl Into<String>, material_condition: ToleranceMaterialCondition) -> Self {
        Self {
            value: value.into(),
            material_condition,
        }
    }
}
