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

    #[inline] pub const fn start_point(&self) -> Vector3 { self.p0 }
    #[inline] pub const fn first_control_point(&self) -> Vector3 { self.p1 }
    #[inline] pub const fn second_control_point(&self) -> Vector3 { self.p2 }
    #[inline] pub const fn end_point(&self) -> Vector3 { self.p3 }

    /// Reverses the direction of the cubic Bezier curve in place.
    pub fn reverse(&mut self) {
        std::mem::swap(&mut self.p0, &mut self.p3);
        std::mem::swap(&mut self.p1, &mut self.p2);
    }

    /// Splits the cubic Bezier curve at `t` in `[0, 1]` using De Casteljau's algorithm.
    pub fn split(&self, t: f64) -> (Self, Self) {
        let lerp = |a: Vector3, b: Vector3| a + (b - a) * t;
        let p01 = lerp(self.p0, self.p1);
        let p12 = lerp(self.p1, self.p2);
        let p23 = lerp(self.p2, self.p3);
        let p012 = lerp(p01, p12);
        let p123 = lerp(p12, p23);
        let p0123 = lerp(p012, p123);
        (
            Self::new(self.p0, p01, p012, p0123),
            Self::new(p0123, p123, p23, self.p3),
        )
    }

    /// Fits a piecewise sequence of C1-smooth cubic Bezier curves through `fit_points`.
    pub fn create_from_fit_points(fit_points: &[Vector3], tension: f64) -> Vec<Self> {
        let n = fit_points.len();
        if n < 2 {
            return Vec::new();
        }
        let mut tangents = vec![Vector3::ZERO; n];
        for i in 0..n {
            if i == 0 {
                tangents[i] = (fit_points[1] - fit_points[0]) * tension;
            } else if i == n - 1 {
                tangents[i] = (fit_points[n - 1] - fit_points[n - 2]) * tension;
            } else {
                tangents[i] = (fit_points[i + 1] - fit_points[i - 1]) * (0.5 * tension);
            }
        }
        let mut curves = Vec::with_capacity(n - 1);
        for i in 0..(n - 1) {
            let p0 = fit_points[i];
            let p3 = fit_points[i + 1];
            let p1 = p0 + tangents[i] / 3.0;
            let p2 = p3 - tangents[i + 1] / 3.0;
            curves.push(Self::new(p0, p1, p2, p3));
        }
        curves
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
