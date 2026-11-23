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
}
