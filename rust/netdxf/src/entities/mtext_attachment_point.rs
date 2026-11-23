/// Attachment point for `MText` entities (group code 71).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum MTextAttachmentPoint {
    #[default]
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 3,
    MiddleLeft = 4,
    MiddleCenter = 5,
    MiddleRight = 6,
    BottomLeft = 7,
    BottomCenter = 8,
    BottomRight = 9,
}

impl MTextAttachmentPoint {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            1 => Self::TopLeft,
            2 => Self::TopCenter,
            3 => Self::TopRight,
            4 => Self::MiddleLeft,
            5 => Self::MiddleCenter,
            6 => Self::MiddleRight,
            7 => Self::BottomLeft,
            8 => Self::BottomCenter,
            9 => Self::BottomRight,
            _ => return None,
        })
    }
}

/// Drawing direction for `MText` entities (group code 72).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum MTextDrawingDirection {
    LeftToRight = 1,
    TopToBottom = 3,
    #[default]
    ByStyle = 5,
}

impl MTextDrawingDirection {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            1 => Self::LeftToRight,
            3 => Self::TopToBottom,
            5 => Self::ByStyle,
            _ => return None,
        })
    }
}

/// Line spacing style for `MText` entities (group code 73).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum MTextLineSpacingStyle {
    #[default]
    AtLeast = 1,
    Exact = 2,
}

impl MTextLineSpacingStyle {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            1 => Self::AtLeast,
            2 => Self::Exact,
            _ => return None,
        })
    }
}
