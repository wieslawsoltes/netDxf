/// Paragraph vertical alignment for `MText`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub enum MTextParagraphVerticalAlignment {
    Bottom = 0,
    #[default]
    Center = 1,
    Top = 2,
}
