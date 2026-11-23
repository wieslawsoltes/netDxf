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
    pub color_name: Option<String>,
    pub transparency: Option<Transparency>,
}

impl Default for MTextBackgroundFill {
    fn default() -> Self {
        Self {
            flags: MTextBackgroundFillFlags::NONE,
            scale_factor: 1.5,
            color: None,
            color_name: None,
            transparency: None,
        }
    }
}

impl MTextBackgroundFill {
    pub fn from_color(color: AciColor, scale_factor: f64) -> Self {
        Self {
            flags: MTextBackgroundFillFlags::USE_COLOR,
            scale_factor: scale_factor.clamp(1.0, 5.0),
            color: Some(color),
            color_name: None,
            transparency: None,
        }
    }

    pub fn from_drawing_window(scale_factor: f64) -> Self {
        Self {
            flags: MTextBackgroundFillFlags::USE_COLOR
                | MTextBackgroundFillFlags::USE_DRAWING_WINDOW_COLOR,
            scale_factor: scale_factor.clamp(1.0, 5.0),
            color: None,
            color_name: None,
            transparency: None,
        }
    }

    pub fn create_text_frame(scale_factor: f64) -> Self {
        Self {
            flags: MTextBackgroundFillFlags::TEXT_FRAME,
            scale_factor: scale_factor.clamp(1.0, 5.0),
            color: None,
            color_name: None,
            transparency: None,
        }
    }

    pub fn color_index(&self) -> Option<i16> {
        self.color.map(|c| c.index)
    }

    pub fn true_color(&self) -> Option<i32> {
        self.color
            .filter(|c| c.use_true_color)
            .map(AciColor::to_true_color)
    }
}
