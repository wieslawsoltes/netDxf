/// A unique knot value with its multiplicity (`netDxf.GTE.UniqueKnot`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct UniqueKnot {
    pub t: f64,
    pub multiplicity: usize,
}

impl UniqueKnot {
    #[inline]
    pub const fn new(t: f64, multiplicity: usize) -> Self {
        Self { t, multiplicity }
    }
}

/// Configuration for a B-spline `BasisFunction` (`netDxf.GTE.BasisFunctionInput`).
#[derive(Clone, Debug, PartialEq)]
pub struct BasisFunctionInput {
    pub num_controls: usize,
    pub degree: usize,
    pub uniform: bool,
    pub periodic: bool,
    pub unique_knots: Vec<UniqueKnot>,
}

impl BasisFunctionInput {
    /// Creates an open uniform clamped knot vector on `[0, 1]`.
    pub fn open_uniform(num_controls: usize, degree: usize) -> Self {
        let num_unique = num_controls.saturating_sub(degree) + 1;
        let mut unique_knots = Vec::with_capacity(num_unique);
        if num_unique >= 2 {
            unique_knots.push(UniqueKnot::new(0.0, degree + 1));
            for i in 1..(num_unique - 1) {
                unique_knots.push(UniqueKnot::new(i as f64 / (num_unique - 1) as f64, 1));
            }
            unique_knots.push(UniqueKnot::new(1.0, degree + 1));
        }
        Self {
            num_controls,
            degree,
            uniform: true,
            periodic: false,
            unique_knots,
        }
    }
}

/// B-spline basis function evaluator supporting Cox-de Boor values and derivatives (`netDxf.GTE.BasisFunction`).
#[derive(Clone, Debug, PartialEq)]
pub struct BasisFunction {
    pub num_controls: usize,
    pub degree: usize,
    pub periodic: bool,
    pub knots: Vec<f64>,
}

impl BasisFunction {
    pub fn new(input: &BasisFunctionInput) -> Self {
        let mut knots = Vec::new();
        for uk in &input.unique_knots {
            for _ in 0..uk.multiplicity {
                knots.push(uk.t);
            }
        }
        Self {
            num_controls: input.num_controls,
            degree: input.degree,
            periodic: input.periodic,
            knots,
        }
    }

    pub fn from_knots(num_controls: usize, degree: usize, knots: Vec<f64>) -> Self {
        Self {
            num_controls,
            degree,
            periodic: false,
            knots,
        }
    }

    #[inline]
    pub fn min_domain(&self) -> f64 {
        self.knots.get(self.degree).copied().unwrap_or(0.0)
    }

    #[inline]
    pub fn max_domain(&self) -> f64 {
        self.knots.get(self.num_controls).copied().unwrap_or(1.0)
    }

    /// Finds the knot span index `span` for parameter `t`.
    pub fn find_span(&self, t: f64) -> usize {
        let n = self.num_controls;
        let p = self.degree;
        if t >= self.knots[n] {
            return n - 1;
        }
        if t <= self.knots[p] {
            return p;
        }
        let mut low = p;
        let mut high = n;
        let mut mid = (low + high) / 2;
        while t < self.knots[mid] || t >= self.knots[mid + 1] {
            if t < self.knots[mid] {
                high = mid;
            } else {
                low = mid;
            }
            mid = (low + high) / 2;
            if low >= high {
                break;
            }
        }
        mid
    }

    /// Evaluates non-vanishing basis functions `N_{span-p, p}(t) .. N_{span, p}(t)`
    /// and their derivatives up to `order` (returns `(span, ders)` where `ders[k][j]` is the k-th derivative).
    pub fn evaluate_derivatives(&self, t: f64, order: usize) -> (usize, Vec<Vec<f64>>) {
        let p = self.degree;
        let k_max = order.min(p);
        let u = t.clamp(self.min_domain(), self.max_domain());
        let span = self.find_span(u);

        let mut ndu = vec![vec![0.0; p + 1]; p + 1];
        let mut left = vec![0.0; p + 1];
        let mut right = vec![0.0; p + 1];
        ndu[0][0] = 1.0;

        for j in 1..=p {
            left[j] = u - self.knots[span + 1 - j];
            right[j] = self.knots[span + j] - u;
            let mut saved = 0.0;
            for r in 0..j {
                ndu[j][r] = right[r + 1] + left[j - r];
                let temp = if ndu[j][r].abs() > 0.0 {
                    ndu[r][j - 1] / ndu[j][r]
                } else {
                    0.0
                };
                ndu[r][j] = saved + right[r + 1] * temp;
                saved = left[j - r] * temp;
            }
            ndu[j][j] = saved;
        }

        let mut ders = vec![vec![0.0; p + 1]; k_max + 1];
        for (j, ndu_row) in ndu.iter().enumerate().take(p + 1) {
            ders[0][j] = ndu_row[p];
        }

        let mut a = vec![vec![0.0; p + 1]; 2];
        for r in 0..=p {
            let mut s1 = 0usize;
            let mut s2 = 1usize;
            a[0][0] = 1.0;
            for (k, ders_row) in ders.iter_mut().enumerate().take(k_max + 1).skip(1) {
                let mut d = 0.0;
                let rk = r as isize - k as isize;
                let pk = (p - k) as isize;
                if r >= k {
                    let denom = ndu[(pk + 1) as usize][rk as usize];
                    a[s2][0] = if denom.abs() > 0.0 { a[s1][0] / denom } else { 0.0 };
                    d = a[s2][0] * ndu[rk as usize][pk as usize];
                }
                let j1 = if rk >= -1 { 1 } else { (-rk) as usize };
                let j2 = if (r as isize - 1) <= pk {
                    k - 1
                } else {
                    p - r
                };
                for j in j1..=j2 {
                    let denom = ndu[(pk + 1) as usize][(rk + j as isize) as usize];
                    a[s2][j] = if denom.abs() > 0.0 {
                        (a[s1][j] - a[s1][j - 1]) / denom
                    } else {
                        0.0
                    };
                    d += a[s2][j] * ndu[(rk + j as isize) as usize][pk as usize];
                }
                if r <= pk as usize {
                    let denom = ndu[(pk + 1) as usize][r];
                    a[s2][k] = if denom.abs() > 0.0 { -a[s1][k - 1] / denom } else { 0.0 };
                    d += a[s2][k] * ndu[r][pk as usize];
                }
                ders_row[r] = d;
                std::mem::swap(&mut s1, &mut s2);
            }
        }

        let mut r = p as f64;
        for row in ders.iter_mut().skip(1) {
            for val in row.iter_mut() {
                *val *= r;
            }
            r *= (p - 1).max(1) as f64;
        }

        (span, ders)
    }

    /// Evaluates the i-th basis function `N_{i, p}(t)` directly.
    pub fn get_value(&self, i: usize, t: f64) -> f64 {
        let (span, ders) = self.evaluate_derivatives(t, 0);
        let min_idx = span.saturating_sub(self.degree);
        if i >= min_idx && i <= span {
            ders[0][i - min_idx]
        } else {
            0.0
        }
    }
}
