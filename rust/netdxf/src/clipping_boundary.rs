use crate::clipping_boundary_type::ClippingBoundaryType;
use crate::vector2::Vector2;

/// Represents a clipping boundary (rectangular or polygonal) in 2D.
#[derive(Clone, Debug, PartialEq)]
pub struct ClippingBoundary {
    pub boundary_type: ClippingBoundaryType,
    pub vertexes: Vec<Vector2>,
}

impl ClippingBoundary {
    pub fn rectangular(first: Vector2, second: Vector2) -> Self {
        Self {
            boundary_type: ClippingBoundaryType::Rectangular,
            vertexes: vec![first, second],
        }
    }

    pub fn polygonal(vertexes: Vec<Vector2>) -> Self {
        Self {
            boundary_type: ClippingBoundaryType::Polygonal,
            vertexes,
        }
    }
}
