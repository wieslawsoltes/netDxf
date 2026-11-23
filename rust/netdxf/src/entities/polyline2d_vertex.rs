use crate::vector2::Vector2;

/// Represents a vertex of a `Polyline2D` (`LWPOLYLINE`).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Polyline2DVertex {
    pub position: Vector2,
    pub start_width: f64,
    pub end_width: f64,
    pub bulge: f64,
}

impl Polyline2DVertex {
    #[inline]
    pub const fn new(x: f64, y: f64) -> Self {
        Self {
            position: Vector2::new(x, y),
            start_width: 0.0,
            end_width: 0.0,
            bulge: 0.0,
        }
    }

    #[inline]
    pub const fn with_bulge(x: f64, y: f64, bulge: f64) -> Self {
        Self {
            position: Vector2::new(x, y),
            start_width: 0.0,
            end_width: 0.0,
            bulge,
        }
    }

    #[inline]
    pub const fn from_position(position: Vector2) -> Self {
        Self {
            position,
            start_width: 0.0,
            end_width: 0.0,
            bulge: 0.0,
        }
    }
}
