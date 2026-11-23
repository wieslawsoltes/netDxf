/// Defines the linetype segment type: simple, text, or shape.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum LinetypeSegmentType {
    /// Linetype segment made only of a simple dash, dot, or space.
    Simple,
    /// Linetype segment made of a text string.
    Text,
    /// Linetype segment made of a shape.
    Shape,
}
