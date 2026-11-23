use crate::dxf_object_code::DxfObjectCode;
use crate::entities::acis_entity::AcisEntityData;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::error::Result;

/// Represents an ACIS `BODY` entity (`netDxf.Entities.Body`).
#[derive(Clone, Debug, PartialEq)]
pub struct Body {
    pub common: EntityCommon,
    pub acis: AcisEntityData,
}

impl Body {
    pub fn new() -> Self {
        Self {
            common: EntityCommon::new(EntityType::Body, DxfObjectCode::BODY),
            acis: AcisEntityData::new(),
        }
    }

    pub fn from_sat_lines<S: AsRef<str>>(lines: impl IntoIterator<Item = S>) -> Result<Self> {
        let mut b = Self::new();
        b.acis.set_sat_lines(lines)?;
        Ok(b)
    }
}

impl Default for Body {
    fn default() -> Self {
        Self::new()
    }
}
