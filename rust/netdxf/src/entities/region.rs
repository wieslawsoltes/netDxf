use crate::dxf_object_code::DxfObjectCode;
use crate::entities::acis_entity::AcisEntityData;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::error::Result;

/// Represents an ACIS `REGION` entity (`netDxf.Entities.Region`).
#[derive(Clone, Debug, PartialEq)]
pub struct Region {
    pub common: EntityCommon,
    pub acis: AcisEntityData,
}

impl Region {
    pub fn new() -> Self {
        Self {
            common: EntityCommon::new(EntityType::Region, DxfObjectCode::REGION),
            acis: AcisEntityData::new(),
        }
    }

    pub fn from_sat_lines<S: AsRef<str>>(lines: impl IntoIterator<Item = S>) -> Result<Self> {
        let mut r = Self::new();
        r.acis.set_sat_lines(lines)?;
        Ok(r)
    }
}

impl Default for Region {
    fn default() -> Self {
        Self::new()
    }
}
