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

    pub fn transform_by(&mut self, transformation: crate::Matrix3, translation: crate::Vector3) {
        let new_normal =
            crate::Vector3::normalize_finite_direction(transformation * self.common.normal)
                .unwrap_or(crate::Vector3::UNIT_Z);
        for v in &mut self.clipping_boundary.vertexes {
            let wcs = transformation
                * crate::MathHelper::transform_ocs_to_wcs(*v, self.common.normal, self.elevation)
                + translation;
            let (ocs, elev) = crate::MathHelper::transform_wcs_to_ocs(wcs, new_normal);
            *v = ocs;
            self.elevation = elev;
        }
        self.common.normal = new_normal;
    }
}
