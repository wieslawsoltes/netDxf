use crate::gte::RootBisection;

/// Real root finder for polynomials `c[0] + c[1]*x + ... + c[n]*x^n = 0`.
pub struct RootsPolynomial;

impl RootsPolynomial {
    /// Evaluates a polynomial `c[0] + c[1]*t + ... + c[n]*t^n` using Horner's method.
    pub fn evaluate(coeffs: &[f64], t: f64) -> f64 {
        let mut acc = 0.0;
        for &c in coeffs.iter().rev() {
            acc = acc * t + c;
        }
        acc
    }

    /// Solves the linear polynomial `c0 + c1*x = 0`.
    pub fn solve_linear(c0: f64, c1: f64) -> Vec<f64> {
        if c1 == 0.0 {
            Vec::new()
        } else {
            vec![-c0 / c1]
        }
    }

    /// Solves the quadratic polynomial `c0 + c1*x + c2*x^2 = 0` for real roots.
    pub fn solve_quadratic(c0: f64, c1: f64, c2: f64) -> Vec<f64> {
        if c2 == 0.0 {
            return Self::solve_linear(c0, c1);
        }
        let disc = c1 * c1 - 4.0 * c0 * c2;
        if disc < 0.0 {
            Vec::new()
        } else if disc == 0.0 {
            vec![-c1 / (2.0 * c2)]
        } else {
            let s = disc.sqrt();
            let mut roots = vec![(-c1 - s) / (2.0 * c2), (-c1 + s) / (2.0 * c2)];
            roots.sort_by(|a, b| a.total_cmp(b));
            roots
        }
    }

    /// Finds real roots of an arbitrary-degree polynomial via derivative recursion and bisection.
    pub fn find(coeffs: &[f64], max_iterations: u32) -> Vec<f64> {
        // Trim trailing zeros
        let mut deg = coeffs.len();
        while deg > 0 && coeffs[deg - 1] == 0.0 {
            deg -= 1;
        }
        if deg <= 1 {
            return Vec::new();
        }
        let c = &coeffs[..deg];
        if deg == 2 {
            return Self::solve_linear(c[0], c[1]);
        }
        if deg == 3 {
            return Self::solve_quadratic(c[0], c[1], c[2]);
        }

        // Cauchy bound for all roots
        let lead = c[deg - 1].abs();
        let max_other = c[..deg - 1]
            .iter()
            .map(|v| v.abs())
            .fold(0.0_f64, f64::max);
        let bound = 1.0 + max_other / lead;

        // Compute derivative coefficients
        let mut deriv = Vec::with_capacity(deg - 1);
        for (i, &ci) in c.iter().enumerate().skip(1) {
            deriv.push(ci * (i as f64));
        }
        let mut critical = Self::find(&deriv, max_iterations);
        critical.retain(|&x| x > -bound && x < bound);

        let mut brackets = Vec::with_capacity(critical.len() + 2);
        brackets.push(-bound);
        brackets.extend(critical);
        brackets.push(bound);

        let mut roots: Vec<f64> = Vec::new();
        for w in brackets.windows(2) {
            let a = w[0];
            let b = w[1];
            let fa = Self::evaluate(c, a);
            let fb = Self::evaluate(c, b);
            if fa.abs() <= 1e-12 {
                if roots.last().map_or(true, |&r| (r - a).abs() > 1e-9) {
                    roots.push(a);
                }
            } else if fa * fb <= 0.0 {
                if let Some(r) = RootBisection::find(
                    |t| Self::evaluate(c, t),
                    a,
                    b,
                    (max_iterations.max(64)) as usize,
                ) {
                    if roots.last().map_or(true, |&prev| (prev - r).abs() > 1e-9) {
                        roots.push(r);
                    }
                }
            }
        }
        if Self::evaluate(c, bound).abs() <= 1e-12
            && roots
                .last()
                .map_or(true, |&prev| (prev - bound).abs() > 1e-9)
        {
            roots.push(bound);
        }
        roots
    }

    /// Solves the cubic polynomial `c0 + c1*x + c2*x^2 + c3*x^3 = 0` for real roots.
    pub fn solve_cubic(c0: f64, c1: f64, c2: f64, c3: f64) -> Vec<f64> {
        Self::find(&[c0, c1, c2, c3], 128)
    }

    /// Solves the quartic polynomial `c0 + c1*x + c2*x^2 + c3*x^3 + c4*x^4 = 0` for real roots.
    pub fn solve_quartic(c0: f64, c1: f64, c2: f64, c3: f64, c4: f64) -> Vec<f64> {
        Self::find(&[c0, c1, c2, c3, c4], 128)
    }

    pub fn get_root_info_quadratic(c0: f64, c1: f64, c2: f64) -> Vec<i32> {
        let roots = Self::solve_quadratic(c0, c1, c2);
        if roots.len() == 1 { vec![2] } else { vec![1; roots.len()] }
    }

    pub fn get_root_info_cubic(c0: f64, c1: f64, c2: f64, c3: f64) -> Vec<i32> {
        vec![1; Self::solve_cubic(c0, c1, c2, c3).len()]
    }

    pub fn get_root_info_quartic(c0: f64, c1: f64, c2: f64, c3: f64, c4: f64) -> Vec<i32> {
        vec![1; Self::solve_quartic(c0, c1, c2, c3, c4).len()]
    }
}
