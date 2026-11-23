use bitflags::bitflags;

bitflags! {
    /// Attribute flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct AttributeFlags: i16 {
        const VISIBLE = 0;
        const HIDDEN = 1;
        const CONSTANT = 2;
        const VERIFY = 4;
        const PREDEFINED = 8;
    }
}
