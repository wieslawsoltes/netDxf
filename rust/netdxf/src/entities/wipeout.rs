use crate::clipping_boundary::ClippingBoundary;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;

/// Represents a DXF `WIPEOUT` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Wipeout {
    pub common: EntityCommon,
    pub clipping_boundary: ClippingBoundary,
    pub elevation: f64,
}

impl Wipeout {
    pub fn new(clipping_boundary: ClippingBoundary) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Wipeout, DxfObjectCode::WIPEOUT),
            clipping_boundary,
            elevation: 0.0,
        }
    }
}
