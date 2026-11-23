use crate::vector2::Vector2;

/// Represents an axis-aligned 2D bounding rectangle.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BoundingRectangle {
    pub min: Vector2,
    pub max: Vector2,
}

impl BoundingRectangle {
    pub fn new(min: Vector2, max: Vector2) -> Self {
        Self {
            min: Vector2::new(min.x.min(max.x), min.y.min(max.y)),
            max: Vector2::new(min.x.max(max.x), min.y.max(max.y)),
        }
    }

    pub fn from_center_radius(center: Vector2, radius: f64) -> Self {
        let r = radius.abs();
        Self {
            min: Vector2::new(center.x - r, center.y - r),
            max: Vector2::new(center.x + r, center.y + r),
        }
    }

    pub fn from_points(points: impl IntoIterator<Item = Vector2>) -> Option<Self> {
        let mut iter = points.into_iter();
        let first = iter.next()?;
        let mut min_x = first.x;
        let mut min_y = first.y;
        let mut max_x = first.x;
        let mut max_y = first.y;
        for p in iter {
            if p.x < min_x { min_x = p.x; }
            if p.y < min_y { min_y = p.y; }
            if p.x > max_x { max_x = p.x; }
            if p.y > max_y { max_y = p.y; }
        }
        Some(Self {
            min: Vector2::new(min_x, min_y),
            max: Vector2::new(max_x, max_y),
        })
    }

    pub fn union(rects: impl IntoIterator<Item = Self>) -> Option<Self> {
        let mut iter = rects.into_iter();
        let mut acc = iter.next()?;
        for r in iter {
            acc.min.x = acc.min.x.min(r.min.x);
            acc.min.y = acc.min.y.min(r.min.y);
            acc.max.x = acc.max.x.max(r.max.x);
            acc.max.y = acc.max.y.max(r.max.y);
        }
        Some(acc)
    }

    #[inline]
    pub fn width(&self) -> f64 {
        self.max.x - self.min.x
    }

    #[inline]
    pub fn height(&self) -> f64 {
        self.max.y - self.min.y
    }

    #[inline]
    pub fn center(&self) -> Vector2 {
        Vector2::mid_point(self.min, self.max)
    }

    #[inline]
    pub fn radius(&self) -> f64 {
        Vector2::distance(self.min, self.max) * 0.5
    }
}
