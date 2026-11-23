use netdxf_shadow::{DiagnosticSeverity, ObjectId, ShadowSnapshot};
use netdxf_shadow_geometry::CadScene;
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DrawingHealthOptions {
    pub tiny_length: f64,
    pub gap_tolerance: f64,
    pub maximum_issues: usize,
    pub maximum_work: i64,
    pub maximum_objects: usize,
    pub maximum_value_depth: usize,
    pub find_duplicates: bool,
    pub find_tiny_gaps: bool,
    pub find_unused_resources: bool,
}

impl Default for DrawingHealthOptions {
    fn default() -> Self {
        Self {
            tiny_length: 1e-7,
            gap_tolerance: 0.01,
            maximum_issues: 10_000,
            maximum_work: 5_000_000,
            maximum_objects: 250_000,
            maximum_value_depth: 64,
            find_duplicates: true,
            find_tiny_gaps: true,
            find_unused_resources: true,
        }
    }
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingHealthResources {
    pub available_shx_fonts: BTreeSet<String>,
    pub available_images: BTreeSet<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DrawingHealthIssue {
    pub id: String,
    pub code: String,
    pub severity: DiagnosticSeverity,
    pub object: Option<ObjectId>,
    pub path: Option<String>,
    pub position: Option<netdxf_shadow::Vec3>,
    pub related_objects: Vec<ObjectId>,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DrawingHealthReport {
    pub document_id: String,
    pub revision: i64,
    pub complete: bool,
    pub work: i64,
    pub coverage: Option<String>,
    pub limitations: Vec<String>,
    pub issues: Vec<DrawingHealthIssue>,
}

/// Evaluates geometry and resource health checks over a [`ShadowSnapshot`] and [`CadScene`].
pub struct DrawingHealth;

impl DrawingHealth {
    pub fn analyze(
        snapshot: &ShadowSnapshot,
        scene: &CadScene,
        options: &DrawingHealthOptions,
        resources: &DrawingHealthResources,
    ) -> DrawingHealthReport {
        let mut issues = Vec::new();
        let mut work = 0i64;

        for (i, occ) in scene.occurrences.iter().enumerate() {
            work += 1;
            if issues.len() >= options.maximum_issues || work >= options.maximum_work {
                return DrawingHealthReport {
                    document_id: snapshot.document_id.clone(),
                    revision: snapshot.revision,
                    complete: false,
                    work,
                    issues,
                    ..Default::default()
                };
            }
            if !occ.bounds.is_empty()
                && occ.bounds.width() < options.tiny_length
                && occ.bounds.height() < options.tiny_length
                && occ.geometry.points.is_empty()
            {
                issues.push(DrawingHealthIssue {
                    id: format!("TINY:{i}"),
                    code: "TINY_GEOMETRY".into(),
                    severity: DiagnosticSeverity::Warning,
                    object: Some(occ.entity.clone()),
                    message: "Entity has degenerate near-zero bounds.".into(),
                    ..Default::default()
                });
            }
            for img in &occ.geometry.images {
                if !resources.available_images.is_empty()
                    && !resources.available_images.contains(&img.file_name)
                {
                    issues.push(DrawingHealthIssue {
                        id: format!("IMG:{i}"),
                        code: "MISSING_IMAGE".into(),
                        severity: DiagnosticSeverity::Warning,
                        object: Some(occ.entity.clone()),
                        message: format!("Referenced image '{}' not found.", img.file_name),
                        ..Default::default()
                    });
                }
            }
        }

        DrawingHealthReport {
            document_id: snapshot.document_id.clone(),
            revision: snapshot.revision,
            complete: true,
            work,
            issues,
            ..Default::default()
        }
    }
}
