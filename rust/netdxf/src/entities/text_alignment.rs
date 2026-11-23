/// Horizontal and vertical alignment for `Text` and `Attribute` entities.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum TextAlignment {
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    #[default]
    BaselineLeft,
    BaselineCenter,
    BaselineRight,
    Aligned,
    Middle,
    Fit,
}

impl TextAlignment {
    pub const fn to_dxf_codes(self) -> (i16, i16) {
        match self {
            Self::TopLeft => (0, 3),
            Self::TopCenter => (1, 3),
            Self::TopRight => (2, 3),
            Self::MiddleLeft => (0, 2),
            Self::MiddleCenter => (1, 2),
            Self::MiddleRight => (2, 2),
            Self::BottomLeft => (0, 1),
            Self::BottomCenter => (1, 1),
            Self::BottomRight => (2, 1),
            Self::BaselineLeft => (0, 0),
            Self::BaselineCenter => (1, 0),
            Self::BaselineRight => (2, 0),
            Self::Aligned => (3, 0),
            Self::Middle => (4, 0),
            Self::Fit => (5, 0),
        }
    }

    pub const fn from_dxf_codes(h: i16, v: i16) -> Self {
        match (h, v) {
            (0, 3) => Self::TopLeft,
            (1, 3) => Self::TopCenter,
            (2, 3) => Self::TopRight,
            (0, 2) => Self::MiddleLeft,
            (1, 2) => Self::MiddleCenter,
            (2, 2) => Self::MiddleRight,
            (0, 1) => Self::BottomLeft,
            (1, 1) => Self::BottomCenter,
            (2, 1) => Self::BottomRight,
            (1, 0) => Self::BaselineCenter,
            (2, 0) => Self::BaselineRight,
            (3, 0) => Self::Aligned,
            (4, 0) => Self::Middle,
            (5, 0) => Self::Fit,
            _ => Self::BaselineLeft,
        }
    }
}
