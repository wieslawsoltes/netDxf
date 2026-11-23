use bitflags::bitflags;

bitflags! {
    /// Standard DXF layer flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct LayerFlags: i16 {
        const NONE = 0;
        const FROZEN = 1;
        const FROZEN_IN_NEW_VIEWPORTS = 2;
        const LOCKED = 4;
        const XREF_DEPENDENT = 16;
        const XREF_RESOLVED = 32;
        const REFERENCED = 64;
    }
}
