use crate::vector3::Vector3;

/// Represents a cubic Bezier curve defined by 4 control points.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BezierCurveCubic {
    pub p0: Vector3,
    pub p1: Vector3,
    pub p2: Vector3,
    pub p3: Vector3,
}

impl BezierCurveCubic {
    #[inline]
    pub const fn new(p0: Vector3, p1: Vector3, p2: Vector3, p3: Vector3) -> Self {
        Self { p0, p1, p2, p3 }
    }

    #[inline]
    pub fn calculate_point(&self, t: f64) -> Vector3 {
        let u = 1.0 - t;
        let uu = u * u;
        let tt = t * t;
        self.p0 * (uu * u)
            + self.p1 * (3.0 * uu * t)
            + self.p2 * (3.0 * u * tt)
            + self.p3 * (tt * t)
    }

    #[inline]
    pub fn calculate_tangent(&self, t: f64) -> Vector3 {
        let u = 1.0 - t;
        (self.p1 - self.p0) * (3.0 * u * u)
            + (self.p2 - self.p1) * (6.0 * u * t)
            + (self.p3 - self.p2) * (3.0 * t * t)
    }

    pub fn polygonal_vertexes(&self, precision: usize) -> Vec<Vector3> {
        let n = precision.max(1);
        let mut pts = Vec::with_capacity(n + 1);
        let step = 1.0 / n as f64;
        for i in 0..=n {
            pts.push(self.calculate_point(i as f64 * step));
        }
        pts
    }
}
