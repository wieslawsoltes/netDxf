use crate::RenderContour;

/// Splits contours at explicit pattern-break indices without mutating original semantic contours
/// (`LinetypePaths.cs`).
pub struct LinetypePaths;

impl LinetypePaths {
    pub fn split(
        contours: &[RenderContour],
        pattern_breaks: &[&[usize]],
        maximum_points: usize,
    ) -> Vec<RenderContour> {
        if contours.is_empty() || maximum_points == 0 {
            return Vec::new();
        }
        let mut output = Vec::with_capacity(contours.len());
        let mut total_points = 0usize;

        for (idx, contour) in contours.iter().enumerate() {
            let breaks = pattern_breaks.get(idx).copied().unwrap_or(&[]);
            if breaks.is_empty() || contour.points.len() < 2 {
                total_points += contour.points.len();
                if total_points > maximum_points {
                    break;
                }
                output.push(contour.clone());
                continue;
            }

            let mut start = 0usize;
            for &boundary in breaks {
                if boundary <= start || boundary >= contour.points.len() - 1 {
                    continue;
                }
                let slice = contour.points[start..=boundary].to_vec();
                total_points += slice.len();
                if total_points > maximum_points {
                    return output;
                }
                output.push(RenderContour {
                    points: slice,
                    closed: false,
                });
                start = boundary;
            }
            let mut tail = contour.points[start..].to_vec();
            if contour.closed && tail.last() != contour.points.first() {
                if let Some(&first) = contour.points.first() {
                    tail.push(first);
                }
            }
            total_points += tail.len();
            if total_points <= maximum_points {
                output.push(RenderContour {
                    points: tail,
                    closed: false,
                });
            }
        }

        output
    }
}
