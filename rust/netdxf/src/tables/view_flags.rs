use bitflags::bitflags;

bitflags! {
    /// Flags stored in group 70 of a VIEW symbol table record.
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct ViewFlags: i16 {
        const NONE = 0;
        const PAPER_SPACE = 1;
        const EXTERNALLY_DEPENDENT = 16;
        const EXTERNAL_REFERENCE_RESOLVED = 32;
        const REFERENCED = 64;
    }
}
