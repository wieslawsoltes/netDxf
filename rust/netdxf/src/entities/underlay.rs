use bitflags::bitflags;

use crate::clipping_boundary::ClippingBoundary;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Underlay file type (DGN, DWF, PDF).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum UnderlayType {
    DGN,
    DWF,
    #[default]
    PDF,
}

impl UnderlayType {
    pub const fn entity_code(self) -> &'static str {
        match self {
            Self::DGN => DxfObjectCode::UNDERLAY_DGN,
            Self::DWF => DxfObjectCode::UNDERLAY_DWF,
            Self::PDF => DxfObjectCode::UNDERLAY_PDF,
        }
    }
}

bitflags! {
    /// Underlay display flags (group code 280).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct UnderlayDisplayFlags: i16 {
        const CLIPPING_ON = 1;
        const SHOW_UNDERLAY = 2;
        const MONOCHROME = 4;
        const ADJUST_FOR_BACKGROUND = 8;
    }
}

/// Represents a DXF `PDFUNDERLAY` / `DWFUNDERLAY` / `DGNUNDERLAY` entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Underlay {
    pub common: EntityCommon,
    pub underlay_type: UnderlayType,
    pub definition_name: String,
    pub definition_handle: Option<String>,
    pub position: Vector3,
    pub scale: Vector2,
    pub rotation: f64,
    pub contrast: i16,
    pub fade: i16,
    pub display_options: UnderlayDisplayFlags,
    pub clipping_boundary: Option<ClippingBoundary>,
}

impl Underlay {
    pub fn new(underlay_type: UnderlayType, definition_name: impl Into<String>) -> Self {
        Self {
            common: EntityCommon::new(EntityType::Underlay, underlay_type.entity_code()),
            underlay_type,
            definition_name: definition_name.into(),
            definition_handle: None,
            position: Vector3::ZERO,
            scale: Vector2::new(1.0, 1.0),
            rotation: 0.0,
            contrast: 100,
            fade: 0,
            display_options: UnderlayDisplayFlags::SHOW_UNDERLAY
                | UnderlayDisplayFlags::ADJUST_FOR_BACKGROUND,
            clipping_boundary: None,
        }
    }

    #[inline] pub fn definition(&self) -> &str { &self.definition_name }
    #[inline] pub fn scale_z(&self) -> f64 { 1.0 }

    pub fn transform_by(&mut self, transformation: crate::matrix3::Matrix3, translation: Vector3) {
        self.position = transformation * self.position + translation;
    }
}
