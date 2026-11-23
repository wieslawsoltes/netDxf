use bitflags::bitflags;

bitflags! {
    /// Published UCS symbol-table flags (DXF group 70).
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct UcsFlags: i16 {
        const NONE = 0;
        const EXTERNALLY_DEPENDENT = 16;
        const XREF_RESOLVED = 32;
        const REFERENCED = 64;
    }
}
