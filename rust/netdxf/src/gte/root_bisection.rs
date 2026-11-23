/// Bisection root finder (`netDxf.GTE.RootBisection`).
pub struct RootBisection;

impl RootBisection {
    /// Finds a root of `f` in `[t0, t1]` where `f(t0)` and `f(t1)` have opposite signs.
    pub fn find<F: Fn(f64) -> f64>(
        f: F,
        mut t0: f64,
        mut t1: f64,
        max_iterations: usize,
    ) -> Option<f64> {
        let mut f0 = f(t0);
        if f0 == 0.0 {
            return Some(t0);
        }
        let f1 = f(t1);
        if f1 == 0.0 {
            return Some(t1);
        }
        if f0 * f1 > 0.0 {
            return None;
        }
        for _ in 0..max_iterations {
            let mid = 0.5 * (t0 + t1);
            let f_mid = f(mid);
            if f_mid == 0.0 || (t1 - t0).abs() <= 1e-14 {
                return Some(mid);
            }
            if f0 * f_mid < 0.0 {
                t1 = mid;
            } else {
                t0 = mid;
                f0 = f_mid;
            }
        }
        Some(0.5 * (t0 + t1))
    }
}
