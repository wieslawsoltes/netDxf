use crate::vector3::Vector3;

/// Abstract base interface for a parameterized 3D surface `X(u, v)`.
pub trait ParametricSurface {
    /// Maximum jet size (position + 2 first derivatives + 3 second derivatives).
    const SUP_ORDER: usize = 6;

    fn u_min(&self) -> f64;
    fn u_max(&self) -> f64;
    fn v_min(&self) -> f64;
    fn v_max(&self) -> f64;
    fn is_rectangular(&self) -> bool {
        true
    }
    fn is_constructed(&self) -> bool {
        true
    }

    /// Evaluates position and partial derivatives up to `order` (0..=2).
    fn evaluate(&self, u: f64, v: f64, order: usize) -> Vec<Vector3>;

    /// Evaluates the surface position at `(u, v)`.
    fn get_position(&self, u: f64, v: f64) -> Vector3 {
        self.evaluate(u, v, 0)
            .into_iter()
            .next()
            .unwrap_or(Vector3::ZERO)
    }

    /// Evaluates the unit U-tangent at `(u, v)`.
    fn get_u_tangent(&self, u: f64, v: f64) -> Vector3 {
        let jet = self.evaluate(u, v, 1);
        Vector3::normalize(jet.get(1).copied().unwrap_or(Vector3::ZERO))
    }

    /// Evaluates the unit V-tangent at `(u, v)`.
    fn get_v_tangent(&self, u: f64, v: f64) -> Vector3 {
        let jet = self.evaluate(u, v, 1);
        Vector3::normalize(jet.get(2).copied().unwrap_or(Vector3::ZERO))
    }
}
