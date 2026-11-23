use crate::dxf_object_code::DxfObjectCode;
use crate::entities::acis_entity::AcisEntityData;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::error::Result;

/// Represents an ACIS `3DSOLID` entity (`netDxf.Entities.Solid3D`).
#[derive(Clone, Debug, PartialEq)]
pub struct Solid3D {
    pub common: EntityCommon,
    pub acis: AcisEntityData,
    pub history_handle: Option<String>,
}

impl Solid3D {
    pub fn new() -> Self {
        Self {
            common: EntityCommon::new(EntityType::Solid3D, DxfObjectCode::SOLID_3D),
            acis: AcisEntityData::new(),
            history_handle: None,
        }
    }

    pub fn from_sat_lines<S: AsRef<str>>(lines: impl IntoIterator<Item = S>) -> Result<Self> {
        let mut s = Self::new();
        s.acis.set_sat_lines(lines)?;
        Ok(s)
    }
}

impl Default for Solid3D {
    fn default() -> Self {
        Self::new()
    }
}
