use crate::vector3::Vector3;

/// Abstract base interface for a parameterized 3D curve `X(t)`.
pub trait ParametricCurve {
    /// Minimum parameter value.
    fn t_min(&self) -> f64;
    /// Maximum parameter value.
    fn t_max(&self) -> f64;
    /// Whether the curve was successfully constructed.
    fn is_constructed(&self) -> bool {
        true
    }
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
}
