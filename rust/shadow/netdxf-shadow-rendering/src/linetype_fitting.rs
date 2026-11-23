use netdxf_shadow_geometry::CadLinetypeAlignment;
use serde::{Deserialize, Serialize};
use std::collections::HashSet;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DashInterval {
    pub start: f64,
    pub end: f64,
}

impl DashInterval {
    pub fn is_dot(&self) -> bool {
        (self.end - self.start).abs() <= 1e-12
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypeFitEvent {
    pub element: usize,
    pub distance: f64,
}

#[derive(Debug, Clone, Copy)]
struct Fit {
    period: f64,
    shift: f64,
    first_delta: f64,
    end_dash: f64,
    cycles: i64,
    solid: bool,
}

/// Shared distance-domain fitting for strokes and linetype decorations (`LinetypeFitting.cs`).
pub struct LinetypeFitting;

impl LinetypeFitting {
    /// Computes dash intervals along `[0, length]` according to `pattern` and `alignment`.
    pub fn strokes(
        length: f64,
        pattern: &[f64],
        alignment: CadLinetypeAlignment,
        closed: bool,
        maximum_segments: usize,
    ) -> Vec<DashInterval> {
        if length <= 0.0 || !length.is_finite() || pattern.is_empty() || maximum_segments == 0 {
            return Vec::new();
        }
        if alignment == CadLinetypeAlignment::Periodic {
            return clip_dash_pattern(0.0, length, pattern, 0.0, maximum_segments);
        }
        let Some(fit) = create_fit(length, pattern, closed) else {
            return vec![DashInterval {
                start: 0.0,
                end: length,
            }];
        };
        if fit.solid {
            return vec![DashInterval {
                start: 0.0,
                end: length,
            }];
        }

        let mut result: Vec<DashInterval> = Vec::new();
        let mut add = |interval: DashInterval| {
            if let Some(last) = result.last_mut() {
                if interval.start <= last.end {
                    last.end = last.end.max(interval.end);
                    return;
                }
            }
            if result.len() < maximum_segments {
                result.push(interval);
            }
        };

        if closed {
            let mut adjusted = pattern.to_vec();
            adjusted[0] += fit.first_delta;
            for iv in clip_dash_pattern(0.0, length, &adjusted, fit.shift, maximum_segments) {
                if !iv.is_dot() || (iv.start - length).abs() > 1e-12 {
                    add(iv);
                }
            }
        } else {
            add(DashInterval {
                start: 0.0,
                end: fit.end_dash,
            });
            for iv in clip_dash_pattern(
                fit.end_dash,
                length - fit.end_dash,
                pattern,
                fit.shift,
                maximum_segments,
            ) {
                add(iv);
            }
            add(DashInterval {
                start: length - fit.end_dash,
                end: length,
            });
        }

        result
    }

    /// Computes decoration placement distances along `[0, length]` for the specified pattern elements.
    pub fn events(
        length: f64,
        pattern: &[f64],
        elements: &[usize],
        alignment: CadLinetypeAlignment,
        closed: bool,
        maximum_placements: usize,
    ) -> Vec<LinetypeFitEvent> {
        if length <= 0.0 || !length.is_finite() || pattern.is_empty() || elements.is_empty() {
            return Vec::new();
        }
        let Some(period) = pattern_period(pattern) else {
            return Vec::new();
        };
        let is_fitted = alignment != CadLinetypeAlignment::Periodic;
        let fit = if is_fitted {
            match create_fit(length, pattern, closed) {
                Some(f) => f,
                None => return Vec::new(),
            }
        } else {
            Fit {
                period,
                shift: 0.0,
                first_delta: 0.0,
                end_dash: 0.0,
                cycles: cycle_count(length, period).unwrap_or(0),
                solid: false,
            }
        };
        if fit.solid {
            return Vec::new();
        }
        let terminal = is_fitted && !closed && elements.contains(&0);
        let requested: HashSet<usize> = elements.iter().copied().collect();
        let mut schedule: Vec<LinetypeFitEvent> = Vec::with_capacity(elements.len());
        let mut offset = 0.0;
        for (i, &val) in pattern.iter().enumerate() {
            offset += val.abs();
            if !requested.contains(&i) {
                continue;
            }
            let mut at = offset + fit.first_delta + fit.shift;
            if closed && at >= fit.period {
                at -= fit.period;
            }
            schedule.push(LinetypeFitEvent {
                element: i,
                distance: at,
            });
        }
        schedule.sort_by(|a, b| {
            a.distance
                .total_cmp(&b.distance)
                .then_with(|| a.element.cmp(&b.element))
        });

        let mut result = Vec::new();
        let end = if is_fitted { fit.cycles } else { fit.cycles + 1 };
        for cycle in 0..end {
            for item in &schedule {
                let at = (cycle as f64) * fit.period + item.distance;
                if at > length || (closed && at >= length) {
                    continue;
                }
                if result.len() >= maximum_placements {
                    return result;
                }
                result.push(LinetypeFitEvent {
                    element: item.element,
                    distance: at,
                });
            }
        }
        if terminal && result.len() < maximum_placements {
            result.push(LinetypeFitEvent {
                element: 0,
                distance: length,
            });
        }
        result
    }
}

pub fn clip_dash_pattern(
    start: f64,
    end: f64,
    pattern: &[f64],
    phase_shift: f64,
    maximum_segments: usize,
) -> Vec<DashInterval> {
    if end <= start || pattern.is_empty() || maximum_segments == 0 {
        return Vec::new();
    }
    let Some(period) = pattern_period(pattern) else {
        return vec![DashInterval { start, end }];
    };

    let mut out = Vec::new();
    let first_cycle = ((start - phase_shift) / period).floor() as i64;
    let last_cycle = ((end - phase_shift) / period).ceil() as i64;

    for cycle in first_cycle..=last_cycle {
        let mut cursor = phase_shift + (cycle as f64) * period;
        for &seg in pattern {
            let seg_len = seg.abs();
            let seg_end = cursor + seg_len;
            if seg >= 0.0 {
                let s = cursor.max(start);
                let e = seg_end.min(end);
                if e >= s && (e > s || (seg == 0.0 && cursor >= start && cursor <= end)) {
                    out.push(DashInterval { start: s, end: e });
                    if out.len() >= maximum_segments {
                        return out;
                    }
                }
            }
            cursor = seg_end;
            if cursor > end {
                break;
            }
        }
    }
    out
}

fn create_fit(length: f64, pattern: &[f64], closed: bool) -> Option<Fit> {
    if length < 0.0 || !length.is_finite() || pattern.len() < 2 || pattern[0] < 0.0 {
        return None;
    }
    let period = pattern_period(pattern)?;
    let mut cycles = cycle_count(length, period)?;
    if cycles == 0 {
        return Some(Fit {
            period,
            shift: 0.0,
            first_delta: 0.0,
            end_dash: length,
            cycles: 0,
            solid: true,
        });
    }
    let mut remainder = (-cycles as f64).mul_add(period, length);
    if remainder < 0.0 {
        cycles -= 1;
        remainder += period;
    }
    if remainder >= period {
        cycles += 1;
        remainder -= period;
    }
    if cycles <= 0 {
        return Some(Fit {
            period,
            shift: 0.0,
            first_delta: 0.0,
            end_dash: length,
            cycles: 0,
            solid: true,
        });
    }
    if closed {
        let extra = remainder / (cycles as f64);
        let first = pattern[0] + extra;
        Some(Fit {
            period: period + extra,
            shift: -first * 0.5,
            first_delta: extra,
            end_dash: first * 0.5,
            cycles,
            solid: false,
        })
    } else {
        let end_dash = remainder * 0.5 + pattern[0] * 0.5;
        Some(Fit {
            period,
            shift: end_dash - pattern[0],
            first_delta: 0.0,
            end_dash,
            cycles,
            solid: false,
        })
    }
}

fn cycle_count(length: f64, period: f64) -> Option<i64> {
    let count = (length / period).floor();
    if !count.is_finite() || count > 4_503_599_627_370_496.0 {
        None
    } else {
        Some(count as i64)
    }
}

fn pattern_period(pattern: &[f64]) -> Option<f64> {
    let mut total = 0.0;
    for &v in pattern {
        let len = v.abs();
        let next = total + len;
        if !next.is_finite() || (len > 0.0 && next <= total) {
            return None;
        }
        total = next;
    }
    if total > 0.0 {
        Some(total)
    } else {
        None
    }
}
