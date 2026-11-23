use crate::aci_color::AciColor;
use crate::transparency::Transparency;
use bitflags::bitflags;

bitflags! {
    /// Background fill flags for an `MText` entity (DXF group 90).
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct MTextBackgroundFillFlags: i32 {
        const NONE = 0;
        const USE_COLOR = 1;
        const USE_DRAWING_WINDOW_COLOR = 2;
        const TEXT_FRAME = 16;
    }
}

/// Stored background fill settings for an `MText` entity.
#[derive(Debug, Clone, PartialEq)]
pub struct MTextBackgroundFill {
    pub flags: MTextBackgroundFillFlags,
    pub scale_factor: f64,
    pub color: Option<AciColor>,
    pub transparency: Option<Transparency>,
}

impl Default for MTextBackgroundFill {
    fn default() -> Self {
        Self {
            flags: MTextBackgroundFillFlags::NONE,
            scale_factor: 1.5,
            color: None,
            transparency: None,
        }
    }
}
