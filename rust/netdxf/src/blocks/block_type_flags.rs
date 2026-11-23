use bitflags::bitflags;

bitflags! {
    /// Block-type flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct BlockTypeFlags: i16 {
        const NONE = 0;
        const ANONYMOUS_BLOCK = 1;
        const NON_CONSTANT_ATTRIBUTE_DEFINITIONS = 2;
        const XREF = 4;
        const XREF_OVERLAY = 8;
        const EXTERNALLY_DEPENDENT = 16;
        const RESOLVED_EXTERNAL_REFERENCE = 32;
        const DEFINITION_EXTERNAL_REFERENCE = 64;
    }
}
