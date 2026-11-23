use crate::vector2::Vector2;

/// Rotation type for complex linetype segments.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum LinetypeSegmentRotationType {
    #[default]
    Relative,
    Absolute,
    Upright,
}

/// A segment in a DXF linetype pattern.
#[derive(Clone, Debug, PartialEq)]
pub enum LinetypeSegment {
    Simple {
        length: f64,
    },
    Text {
        length: f64,
        text: String,
        style: String,
        offset: Vector2,
        rotation_type: LinetypeSegmentRotationType,
        rotation: f64,
        scale: f64,
    },
    Shape {
        length: f64,
        name: String,
        shape_number: i16,
        style: String,
        offset: Vector2,
        rotation_type: LinetypeSegmentRotationType,
        rotation: f64,
        scale: f64,
    },
}

impl LinetypeSegment {
    #[inline]
    pub fn length(&self) -> f64 {
        match self {
            Self::Simple { length }
            | Self::Text { length, .. }
            | Self::Shape { length, .. } => *length,
        }
    }
}
