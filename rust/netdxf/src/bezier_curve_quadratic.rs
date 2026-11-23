use crate::vector3::Vector3;

/// Represents a quadratic Bezier curve defined by 3 control points.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BezierCurveQuadratic {
    pub p0: Vector3,
    pub p1: Vector3,
    pub p2: Vector3,
}

impl BezierCurveQuadratic {
    #[inline]
    pub const fn new(p0: Vector3, p1: Vector3, p2: Vector3) -> Self {
        Self { p0, p1, p2 }
    }

    #[inline]
    pub const fn start_point(&self) -> Vector3 {
        self.p0
    }

    #[inline]
    pub const fn control_point(&self) -> Vector3 {
        self.p1
    }

    #[inline]
    pub const fn end_point(&self) -> Vector3 {
        self.p2
    }

    pub fn reverse(&mut self) {
        std::mem::swap(&mut self.p0, &mut self.p2);
    }

    /// Splits this quadratic Bezier curve at parameter `t` in `[0, 1]` using de Casteljau's algorithm.
    pub fn split(&self, t: f64) -> (Self, Self) {
        let q0 = self.p0 + (self.p1 - self.p0) * t;
        let q1 = self.p1 + (self.p2 - self.p1) * t;
        let r = q0 + (q1 - q0) * t;
        (Self::new(self.p0, q0, r), Self::new(r, q1, self.p2))
    }

    #[inline]
    pub fn calculate_point(&self, t: f64) -> Vector3 {
        let u = 1.0 - t;
        self.p0 * (u * u) + self.p1 * (2.0 * u * t) + self.p2 * (t * t)
    }

    #[inline]
    pub fn calculate_tangent(&self, t: f64) -> Vector3 {
        let u = 1.0 - t;
        (self.p1 - self.p0) * (2.0 * u) + (self.p2 - self.p1) * (2.0 * t)
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
