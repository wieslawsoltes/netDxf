use bitflags::bitflags;

bitflags! {
    /// Stored VPORT symbol-table flags (DXF group 70).
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct VPortFlags: i16 {
        const NONE = 0;
        const EXTERNALLY_DEPENDENT = 16;
        const EXTERNAL_REFERENCE_RESOLVED = 32;
        const REFERENCED = 64;
    }
}
