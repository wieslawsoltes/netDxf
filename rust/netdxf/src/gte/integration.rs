/// Numerical integration methods (`netDxf.GTE.Integration`).
pub struct Integration;

impl Integration {
    /// Romberg integration of `f` on `[a, b]` with `order` Richardson extrapolation levels.
    pub fn romberg<F: Fn(f64) -> f64>(order: usize, a: f64, b: f64, f: F) -> f64 {
        let ord = order.max(1);
        let mut rom0 = vec![0.0; ord];
        let mut rom1 = vec![0.0; ord];
        let mut h = b - a;
        rom0[0] = 0.5 * h * (f(a) + f(b));

        let mut p0 = 1usize;
        for i0 in 2..=ord {
            let mut sum = 0.0;
            for i1 in 1..=p0 {
                sum += f(a + h * (i1 as f64 - 0.5));
            }
            rom1[0] = 0.5 * (rom0[0] + h * sum);

            let mut p2 = 4.0;
            for i2 in 1..i0 {
                rom1[i2] = (p2 * rom1[i2 - 1] - rom0[i2 - 1]) / (p2 - 1.0);
                p2 *= 4.0;
            }
            rom0[..i0].copy_from_slice(&rom1[..i0]);
            p0 <<= 1;
            h *= 0.5;
        }
        rom0[ord - 1]
    }

    /// Composite Simpson's rule integration of `f` on `[a, b]` with `num_subintervals` (even).
    pub fn simpson<F: Fn(f64) -> f64>(num_subintervals: usize, a: f64, b: f64, f: F) -> f64 {
        let n = (num_subintervals.max(2) + 1) & !1;
        let h = (b - a) / n as f64;
        let mut sum = f(a) + f(b);
        for i in 1..n {
            let x = a + h * i as f64;
            sum += if i % 2 == 0 { 2.0 } else { 4.0 } * f(x);
        }
        sum * h / 3.0
    }

    pub const NUM_BITS: usize = 64;

    /// Composite trapezoid rule integration of `f` on `[a, b]` with `num_samples` sample points.
    pub fn trapezoid_rule<F: Fn(f64) -> f64>(num_samples: usize, a: f64, b: f64, f: F) -> f64 {
        let n = num_samples.max(2);
        let h = (b - a) / (n - 1) as f64;
        let mut sum = 0.5 * (f(a) + f(b));
        for i in 1..(n - 1) {
            sum += f(a + i as f64 * h);
        }
        sum * h
    }

    /// Computes Gauss-Legendre quadrature nodes and weights on `[-1, 1]` for `degree` points.
    pub fn compute_quadrature_info(degree: usize) -> (Vec<f64>, Vec<f64>) {
        let n = degree.max(1);
        let mut roots = vec![0.0; n];
        let mut weights = vec![0.0; n];
        let m = n.div_ceil(2);
        for i in 0..m {
            let mut z = (std::f64::consts::PI * (i as f64 + 0.75) / (n as f64 + 0.5)).cos();
            let mut pp = 0.0;
            for _ in 0..32 {
                let mut p1 = 1.0;
                let mut p2 = 0.0;
                for j in 1..=n {
                    let p3 = p2;
                    p2 = p1;
                    p1 = ((2 * j - 1) as f64 * z * p2 - (j - 1) as f64 * p3) / j as f64;
                }
                pp = n as f64 * (z * p1 - p2) / (z * z - 1.0);
                let z1 = z;
                z = z1 - p1 / pp;
                if (z - z1).abs() <= 1e-15 {
                    break;
                }
            }
            roots[i] = -z;
            roots[n - 1 - i] = z;
            let w = 2.0 / ((1.0 - z * z) * pp * pp);
            weights[i] = w;
            weights[n - 1 - i] = w;
        }
        (roots, weights)
    }

    /// Gauss-Legendre quadrature integration of `f` on `[a, b]` using `roots` and `weights` on `[-1, 1]`.
    pub fn gaussian_quadrature<F: Fn(f64) -> f64>(
        roots: &[f64],
        weights: &[f64],
        a: f64,
        b: f64,
        f: F,
    ) -> f64 {
        let half = 0.5 * (b - a);
        let center = 0.5 * (b + a);
        let mut sum = 0.0;
        for (&r, &w) in roots.iter().zip(weights.iter()) {
            sum += w * f(center + half * r);
        }
        half * sum
    }

    pub fn product(a: &[f64], b: &[f64]) -> f64 {
        a.iter().zip(b.iter()).map(|(x, y)| x * y).sum()
    }
}
