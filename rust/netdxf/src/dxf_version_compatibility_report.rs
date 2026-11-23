use crate::dxf_document::DxfDocument;
use crate::entities::EntityObject;
use crate::header::DxfVersion;

/// The known writer behavior identified by a target-version diagnostic.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum DxfVersionCompatibilityKind {
    /// An existing writer guard rejects this stored feature for the target profile.
    WriterRejection,
    /// The current writer omits this stored property for the target profile.
    DataOmission,
}

/// A captured description of one known target-version writer restriction or omission.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DxfVersionCompatibilityDiagnostic {
    pub code: String,
    pub kind: DxfVersionCompatibilityKind,
    pub source_handle: Option<String>,
    pub source_code_name: String,
    pub property_path: String,
    pub message: String,
}

/// Individual compatibility finding produced by `DxfDocument::check_version_compatibility`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DxfVersionCompatibilityIssue {
    pub feature: String,
    pub required_version: DxfVersion,
    pub message: String,
}

/// Version preflight compatibility report (`netDxf.DxfVersionCompatibilityReport`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DxfVersionCompatibilityReport {
    pub source_version: DxfVersion,
    pub target_version: DxfVersion,
    pub issues: Vec<DxfVersionCompatibilityIssue>,
    pub diagnostics: Vec<DxfVersionCompatibilityDiagnostic>,
    pub has_known_rejections: bool,
    pub has_known_losses: bool,
}

impl DxfVersionCompatibilityReport {
    #[inline]
    pub fn is_compatible(&self) -> bool {
        self.issues.is_empty() && self.diagnostics.is_empty()
    }

    /// Evaluates `doc` against `target_version` and reports any features that require a newer DXF version.
    pub fn analyze(doc: &DxfDocument, target_version: DxfVersion) -> Self {
        let mut issues = Vec::new();
        let mut diagnostics = Vec::new();

        if target_version < DxfVersion::AutoCad2004 {
            let has_true_color = doc.layers.iter().any(|l| l.color.use_true_color)
                || doc.entities.iter().any(|e| e.common().color.use_true_color);
            if has_true_color {
                let message = "24-bit TrueColor (group 420) requires AutoCAD 2004 or newer; R2000 falls back to ACI index.".to_string();
                issues.push(DxfVersionCompatibilityIssue {
                    feature: "TrueColor".into(),
                    required_version: DxfVersion::AutoCad2004,
                    message: message.clone(),
                });
                diagnostics.push(DxfVersionCompatibilityDiagnostic {
                    code: "TRUE_COLOR_OMISSION".into(),
                    kind: DxfVersionCompatibilityKind::DataOmission,
                    source_handle: None,
                    source_code_name: "DOCUMENT".into(),
                    property_path: "Color".into(),
                    message,
                });
            }
        }

        if target_version < DxfVersion::AutoCad2007 {
            if doc.sun.is_some() || !doc.sun_studies.is_empty() {
                let message = "SUN and SUNSTUDY objects require AutoCAD 2007 or newer.".to_string();
                issues.push(DxfVersionCompatibilityIssue {
                    feature: "Sun".into(),
                    required_version: DxfVersion::AutoCad2007,
                    message: message.clone(),
                });
                diagnostics.push(DxfVersionCompatibilityDiagnostic {
                    code: "SUN_OMISSION".into(),
                    kind: DxfVersionCompatibilityKind::DataOmission,
                    source_handle: None,
                    source_code_name: "SUN".into(),
                    property_path: "Sun".into(),
                    message,
                });
            }
            if doc.entities.iter().any(|e| matches!(e, EntityObject::Light(_))) {
                let message = "LIGHT entities require AutoCAD 2007 or newer.".to_string();
                issues.push(DxfVersionCompatibilityIssue {
                    feature: "Light".into(),
                    required_version: DxfVersion::AutoCad2007,
                    message: message.clone(),
                });
                diagnostics.push(DxfVersionCompatibilityDiagnostic {
                    code: "LIGHT_OMISSION".into(),
                    kind: DxfVersionCompatibilityKind::DataOmission,
                    source_handle: None,
                    source_code_name: "LIGHT".into(),
                    property_path: "Entities".into(),
                    message,
                });
            }
        }

        if target_version < DxfVersion::AutoCad2010 && doc.geo_data.is_some() {
            let message = "GEODATA objects require AutoCAD 2010 or newer.".to_string();
            issues.push(DxfVersionCompatibilityIssue {
                feature: "GeoData".into(),
                required_version: DxfVersion::AutoCad2010,
                message: message.clone(),
            });
            diagnostics.push(DxfVersionCompatibilityDiagnostic {
                code: "GEODATA_OMISSION".into(),
                kind: DxfVersionCompatibilityKind::DataOmission,
                source_handle: None,
                source_code_name: "GEODATA".into(),
                property_path: "GeoData".into(),
                message,
            });
        }

        if target_version >= DxfVersion::AutoCad2013
            && doc.entities.iter().any(|e| {
                matches!(
                    e,
                    EntityObject::Solid3D(_) | EntityObject::Body(_) | EntityObject::Region(_)
                )
            })
        {
            let message = "Inline SAT ACIS entities (3DSOLID/BODY/REGION) are native to R2000-R2010; R2013+ uses ACDSDATA binary SAB.".to_string();
            issues.push(DxfVersionCompatibilityIssue {
                feature: "AcisSat".into(),
                required_version: DxfVersion::AutoCad2010,
                message: message.clone(),
            });
            diagnostics.push(DxfVersionCompatibilityDiagnostic {
                code: "ACIS_SAT_REJECTION".into(),
                kind: DxfVersionCompatibilityKind::WriterRejection,
                source_handle: None,
                source_code_name: "ACIS".into(),
                property_path: "Entities".into(),
                message,
            });
        }

        diagnostics.sort_by(|a, b| {
            a.source_handle
                .as_deref()
                .unwrap_or("")
                .cmp(b.source_handle.as_deref().unwrap_or(""))
                .then_with(|| a.source_code_name.cmp(&b.source_code_name))
                .then_with(|| a.property_path.cmp(&b.property_path))
                .then_with(|| a.code.cmp(&b.code))
        });

        let has_known_rejections = diagnostics
            .iter()
            .any(|d| d.kind == DxfVersionCompatibilityKind::WriterRejection);
        let has_known_losses = diagnostics
            .iter()
            .any(|d| d.kind == DxfVersionCompatibilityKind::DataOmission);

        Self {
            source_version: doc.drawing_variables.acad_ver,
            target_version,
            issues,
            diagnostics,
            has_known_rejections,
            has_known_losses,
        }
    }
}
