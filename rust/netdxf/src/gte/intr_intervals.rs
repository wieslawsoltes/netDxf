/// Classification of an interval intersection result.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum FIQueryIntervalsType {
    IsEmpty = 0,
    IsPoint = 1,
    IsFinite = 2,
    IsPositiveInfinite = 3,
    IsNegativeInfinite = 4,
    IsDynamicQuery = 5,
}

/// Test-intersection query for 1D intervals (static and dynamic).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TIQueryIntervals {
    pub intersect: bool,
    pub first_time: f64,
    pub last_time: f64,
}

impl TIQueryIntervals {
    /// Static test-intersection between two finite intervals `[t0, t1]`.
    pub fn finite_finite(interval0: [f64; 2], interval1: [f64; 2]) -> Self {
        Self {
            intersect: interval0[0] <= interval1[1] && interval0[1] >= interval1[0],
            first_time: 0.0,
            last_time: 0.0,
        }
    }

    /// Static test-intersection between a finite interval and a semi-infinite interval.
    pub fn finite_semiinfinite(finite: [f64; 2], a: f64, is_positive_infinite: bool) -> Self {
        let intersect = if is_positive_infinite {
            finite[1] >= a
        } else {
            finite[0] <= a
        };
        Self {
            intersect,
            first_time: 0.0,
            last_time: 0.0,
        }
    }

    /// Static test-intersection between two semi-infinite intervals.
    pub fn semiinfinite_semiinfinite(
        a0: f64,
        is_positive_infinite0: bool,
        a1: f64,
        is_positive_infinite1: bool,
    ) -> Self {
        let intersect = match (is_positive_infinite0, is_positive_infinite1) {
            (true, true) | (false, false) => true,
            (true, false) => a0 <= a1,
            (false, true) => a0 >= a1,
        };
        Self {
            intersect,
            first_time: 0.0,
            last_time: 0.0,
        }
    }

    /// Dynamic test-intersection for intervals moving with constant speeds over `[0, max_time]`.
    pub fn dynamic(
        max_time: f64,
        interval0: [f64; 2],
        speed0: f64,
        interval1: [f64; 2],
        speed1: f64,
    ) -> Self {
        if interval0[1] < interval1[0] {
            let diff_speed = speed0 - speed1;
            if diff_speed > 0.0 {
                let diff_pos = interval1[0] - interval0[1];
                return Self {
                    intersect: diff_pos <= max_time * diff_speed,
                    first_time: diff_pos / diff_speed,
                    last_time: (interval1[1] - interval0[0]) / diff_speed,
                };
            }
            Self {
                intersect: false,
                first_time: 0.0,
                last_time: 0.0,
            }
        } else if interval0[0] > interval1[1] {
            let diff_speed = speed1 - speed0;
            if diff_speed > 0.0 {
                let diff_pos = interval0[0] - interval1[1];
                return Self {
                    intersect: diff_pos <= max_time * diff_speed,
                    first_time: diff_pos / diff_speed,
                    last_time: (interval0[1] - interval1[0]) / diff_speed,
                };
            }
            Self {
                intersect: false,
                first_time: 0.0,
                last_time: 0.0,
            }
        } else {
            let diff_speed = speed0 - speed1;
            let last_time = if diff_speed > 0.0 {
                (interval1[1] - interval0[0]) / diff_speed
            } else if diff_speed < 0.0 {
                (interval1[0] - interval0[1]) / diff_speed
            } else {
                f64::INFINITY
            };
            Self {
                intersect: true,
                first_time: 0.0,
                last_time,
            }
        }
    }
}

/// Find-intersection query for 1D intervals (static and dynamic).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct FIQueryIntervals {
    pub intersect: bool,
    pub num_intersections: usize,
    pub overlap: [f64; 2],
    pub query_type: FIQueryIntervalsType,
    pub first_time: f64,
    pub last_time: f64,
}

impl FIQueryIntervals {
    /// Static find-intersection between two finite intervals `[t0, t1]`.
    pub fn finite_finite(interval0: [f64; 2], interval1: [f64; 2]) -> Self {
        if interval0[1] < interval1[0] || interval0[0] > interval1[1] {
            Self {
                intersect: false,
                num_intersections: 0,
                overlap: [0.0, 0.0],
                query_type: FIQueryIntervalsType::IsEmpty,
                first_time: 0.0,
                last_time: 0.0,
            }
        } else if interval0[1] > interval1[0] {
            if interval0[0] < interval1[1] {
                let lo = interval0[0].max(interval1[0]);
                let hi = interval0[1].min(interval1[1]);
                let (num, qtype) = if lo < hi {
                    (2, FIQueryIntervalsType::IsFinite)
                } else {
                    (1, FIQueryIntervalsType::IsPoint)
                };
                Self {
                    intersect: true,
                    num_intersections: num,
                    overlap: [lo, hi],
                    query_type: qtype,
                    first_time: 0.0,
                    last_time: 0.0,
                }
            } else {
                Self {
                    intersect: true,
                    num_intersections: 1,
                    overlap: [interval0[0], interval0[0]],
                    query_type: FIQueryIntervalsType::IsPoint,
                    first_time: 0.0,
                    last_time: 0.0,
                }
            }
        } else {
            Self {
                intersect: true,
                num_intersections: 1,
                overlap: [interval0[1], interval0[1]],
                query_type: FIQueryIntervalsType::IsPoint,
                first_time: 0.0,
                last_time: 0.0,
            }
        }
    }

    /// Dynamic find-intersection between two moving finite intervals.
    pub fn dynamic(
        max_time: f64,
        interval0: [f64; 2],
        speed0: f64,
        interval1: [f64; 2],
        speed1: f64,
    ) -> Self {
        let ti = TIQueryIntervals::dynamic(max_time, interval0, speed0, interval1, speed1);
        if !ti.intersect {
            return Self {
                intersect: false,
                num_intersections: 0,
                overlap: [0.0, 0.0],
                query_type: FIQueryIntervalsType::IsEmpty,
                first_time: ti.first_time,
                last_time: ti.last_time,
            };
        }
        let moved0 = [
            interval0[0] + ti.first_time * speed0,
            interval0[1] + ti.first_time * speed0,
        ];
        let moved1 = [
            interval1[0] + ti.first_time * speed1,
            interval1[1] + ti.first_time * speed1,
        ];
        let mut fi = Self::finite_finite(moved0, moved1);
        fi.query_type = FIQueryIntervalsType::IsDynamicQuery;
        fi.first_time = ti.first_time;
        fi.last_time = ti.last_time;
        fi
    }
}
