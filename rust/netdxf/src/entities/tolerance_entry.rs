use crate::entities::{DatumReferenceValue, ToleranceGeometricSymbol, ToleranceValue};

/// Represents an entry in a tolerance entity.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct ToleranceEntry {
    pub geometric_symbol: ToleranceGeometricSymbol,
    pub tolerance1: Option<ToleranceValue>,
    pub tolerance2: Option<ToleranceValue>,
    pub datum1: Option<DatumReferenceValue>,
    pub datum2: Option<DatumReferenceValue>,
    pub datum3: Option<DatumReferenceValue>,
}

impl ToleranceEntry {
    pub fn new() -> Self {
        Self::default()
    }
}
