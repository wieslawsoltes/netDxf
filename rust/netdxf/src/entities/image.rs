use bitflags::bitflags;

use crate::clipping_boundary::ClippingBoundary;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector3::Vector3;

bitflags! {
    /// Image display flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct ImageDisplayFlags: i16 {
        const SHOW_IMAGE = 1;
        const SHOW_IMAGE_WHEN_NOT_ALIGNED_WITH_SCREEN = 2;
        const USE_CLIPPING_BOUNDARY = 4;
        const TRANSPARENCY_ON = 8;
    }
}

/// Represents a DXF `IMAGE` (raster image) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Image {
    pub common: EntityCommon,
    pub definition_name: String,
    pub definition_handle: Option<String>,
    pub position: Vector3,
    pub width: f64,
    pub height: f64,
    pub rotation: f64,
    pub display_options: ImageDisplayFlags,
    pub clipping: bool,
    pub brightness: i16,
    pub contrast: i16,
    pub fade: i16,
    pub clipping_boundary: Option<ClippingBoundary>,
}

impl Image {
    pub fn new(
        definition_name: impl Into<String>,
        position: Vector3,
        width: f64,
        height: f64,
    ) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Image, DxfObjectCode::IMAGE),
            definition_name: definition_name.into(),
            definition_handle: None,
            position,
            width,
            height,
            rotation: 0.0,
            display_options: ImageDisplayFlags::SHOW_IMAGE
                | ImageDisplayFlags::SHOW_IMAGE_WHEN_NOT_ALIGNED_WITH_SCREEN
                | ImageDisplayFlags::USE_CLIPPING_BOUNDARY,
            clipping: false,
            brightness: 50,
            contrast: 50,
            fade: 0,
            clipping_boundary: None,
        }
    }
}
