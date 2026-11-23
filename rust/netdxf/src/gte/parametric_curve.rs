use crate::gte::integration::Integration;
use crate::gte::root_bisection::RootBisection;
use crate::vector3::Vector3;

/// Default Romberg integration order matching GTE `ParametricCurve.DEFAULT_ROMBERG_ORDER`.
pub const DEFAULT_ROMBERG_ORDER: usize = 8;
/// Default maximum bisections matching GTE `ParametricCurve.DEFAULT_MAX_BISECTIONS`.
pub const DEFAULT_MAX_BISECTIONS: usize = 1024;

/// Abstract base interface for a parameterized 3D curve `X(t)` (`netDxf.GTE.ParametricCurve`).
pub trait ParametricCurve {
    /// Minimum parameter value.
    fn t_min(&self) -> f64;
    /// Maximum parameter value.
    fn t_max(&self) -> f64;
    /// Whether the curve was successfully constructed.
    fn is_constructed(&self) -> bool {
        true
    }
    /// Number of parameter segments (default 1).
    fn num_segments(&self) -> usize {
        1
    }
    /// Segment boundary parameter values.
    fn times(&self) -> Vec<f64> {
        vec![self.t_min(), self.t_max()]
    }
    /// Romberg integration order used by arc-length queries.
    fn romberg_order(&self) -> usize {
        DEFAULT_ROMBERG_ORDER
    }
    /// Maximum bisections used by `get_time`.
    fn max_bisections(&self) -> usize {
        DEFAULT_MAX_BISECTIONS
    }
    /// Updates the time interval for single-segment curves (no-op if unsupported).
    fn set_time_interval(&mut self, _t_min: f64, _t_max: f64) {}

    /// Evaluates position and derivatives up to `order` (0..=3).
    fn evaluate(&self, t: f64, order: usize) -> Vec<Vector3>;

    /// Evaluates the position at parameter `t`.
    fn get_position(&self, t: f64) -> Vector3 {
        self.evaluate(t, 0)
            .into_iter()
            .next()
            .unwrap_or(Vector3::ZERO)
    }

    /// Evaluates the unit tangent vector at parameter `t`.
    fn get_tangent(&self, t: f64) -> Vector3 {
        let jet = self.evaluate(t, 1);
        Vector3::normalize(jet.get(1).copied().unwrap_or(Vector3::ZERO))
    }

    /// Evaluates the speed `|X'(t)|` at parameter `t`.
    fn get_speed(&self, t: f64) -> f64 {
        let jet = self.evaluate(t, 1);
        jet.get(1).map_or(0.0, |v| v.modulus())
    }

    /// Computes arc length over `[t0, t1]` via Romberg integration.
    fn get_length(&self, t0: f64, t1: f64) -> f64 {
        let a = t0.max(self.t_min());
        let b = t1.min(self.t_max());
        if b <= a {
            return 0.0;
        }
        Integration::romberg(self.romberg_order(), a, b, |t| self.get_speed(t))
    }

    /// Computes total arc length over `[t_min, t_max]`.
    fn get_total_length(&self) -> f64 {
        self.get_length(self.t_min(), self.t_max())
    }

    /// Inverse arc-length mapping `t = Length^{-1}(length)` via bisection.
    fn get_time(&self, length: f64) -> f64 {
        let t_min = self.t_min();
        let t_max = self.t_max();
        if length <= 0.0 {
            return t_min;
        }
        let total = self.get_total_length();
        if length >= total || total <= 0.0 {
            return t_max;
        }
        let f = |t: f64| Integration::romberg(self.romberg_order(), t_min, t, |z| self.get_speed(z)) - length;
        let ratio = length / total;
        let t_mid = (1.0 - ratio) * t_min + ratio * t_max;
        let f_mid = f(t_mid);
        if f_mid > 0.0 {
            RootBisection::find(f, t_min, t_mid, self.max_bisections()).unwrap_or(t_mid)
        } else if f_mid < 0.0 {
            RootBisection::find(f, t_mid, t_max, self.max_bisections()).unwrap_or(t_mid)
        } else {
            t_mid
        }
    }

    /// Subdivides the curve uniformly by parameter `t`, returning `(points, ts)`.
    fn subdivide_by_time(&self, num_points: usize) -> (Vec<Vector3>, Vec<f64>) {
        if num_points < 2 {
            let t = self.t_min();
            return (vec![self.get_position(t)], vec![t]);
        }
        let t0 = self.t_min();
        let delta = (self.t_max() - t0) / (num_points - 1) as f64;
        let mut points = Vec::with_capacity(num_points);
        let mut ts = Vec::with_capacity(num_points);
        for i in 0..num_points {
            let t = t0 + delta * i as f64;
            points.push(self.get_position(t));
            ts.push(t);
        }
        (points, ts)
    }

    /// Subdivides the curve uniformly by arc length, returning `(points, ts)`.
    fn subdivide_by_length(&self, num_points: usize) -> (Vec<Vector3>, Vec<f64>) {
        if num_points < 2 {
            let t = self.t_min();
            return (vec![self.get_position(t)], vec![t]);
        }
        let total = self.get_total_length();
        let delta = total / (num_points - 1) as f64;
        let mut points = Vec::with_capacity(num_points);
        let mut ts = Vec::with_capacity(num_points);
        for i in 0..num_points {
            let t = self.get_time(delta * i as f64);
            points.push(self.get_position(t));
            ts.push(t);
        }
        (points, ts)
    }
}
