/// Paragraph horizontal alignment for `MText`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub enum MTextParagraphAlignment {
    #[default]
    Default = 0,
    Left = 1,
    Center = 2,
    Right = 3,
    Justified = 4,
    Distribute = 5,
}
