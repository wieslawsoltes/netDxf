use bitflags::bitflags;

bitflags! {
    /// TrueType font style flags.
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct FontStyle: i32 {
        const REGULAR = 0;
        const ITALIC = 1;
        const BOLD = 2;
    }
}
