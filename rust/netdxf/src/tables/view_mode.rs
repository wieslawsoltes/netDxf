use bitflags::bitflags;

bitflags! {
    /// View mode flags (DXF group 71).
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct ViewModeFlags: i32 {
        const OFF = 0;
        const PERSPECTIVE = 1;
        const FRONT_CLIPPING_PLANE = 2;
        const BACK_CLIPPING_PLANE = 4;
        const UCS_FOLLOW = 8;
        const FRONT_CLIP_NOT_AT_EYE = 16;
    }
}
