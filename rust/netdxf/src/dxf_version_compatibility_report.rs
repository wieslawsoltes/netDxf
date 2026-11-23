use crate::dxf_document::DxfDocument;
use crate::entities::EntityObject;
use crate::header::DxfVersion;

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
    pub target_version: DxfVersion,
    pub issues: Vec<DxfVersionCompatibilityIssue>,
}

impl DxfVersionCompatibilityReport {
    #[inline]
    pub fn is_compatible(&self) -> bool {
        self.issues.is_empty()
    }

    /// Evaluates `doc` against `target_version` and reports any features that require a newer DXF version.
    pub fn analyze(doc: &DxfDocument, target_version: DxfVersion) -> Self {
        let mut issues = Vec::new();

        if target_version < DxfVersion::AutoCad2004 {
            let has_true_color = doc.layers.iter().any(|l| l.color.use_true_color)
                || doc.entities.iter().any(|e| e.common().color.use_true_color);
            if has_true_color {
                issues.push(DxfVersionCompatibilityIssue {
                    feature: "TrueColor".into(),
                    required_version: DxfVersion::AutoCad2004,
                    message: "24-bit TrueColor (group 420) requires AutoCAD 2004 or newer; R2000 falls back to ACI index.".into(),
                });
            }
        }

        if target_version < DxfVersion::AutoCad2007 {
            if doc.sun.is_some() || !doc.sun_studies.is_empty() {
                issues.push(DxfVersionCompatibilityIssue {
                    feature: "Sun".into(),
                    required_version: DxfVersion::AutoCad2007,
                    message: "SUN and SUNSTUDY objects require AutoCAD 2007 or newer.".into(),
                });
            }
            if doc.entities.iter().any(|e| matches!(e, EntityObject::Light(_))) {
                issues.push(DxfVersionCompatibilityIssue {
                    feature: "Light".into(),
                    required_version: DxfVersion::AutoCad2007,
                    message: "LIGHT entities require AutoCAD 2007 or newer.".into(),
                });
            }
        }

        if target_version < DxfVersion::AutoCad2010 && doc.geo_data.is_some() {
            issues.push(DxfVersionCompatibilityIssue {
                feature: "GeoData".into(),
                required_version: DxfVersion::AutoCad2010,
                message: "GEODATA objects require AutoCAD 2010 or newer.".into(),
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
            issues.push(DxfVersionCompatibilityIssue {
                feature: "AcisSat".into(),
                required_version: DxfVersion::AutoCad2010,
                message: "Inline SAT ACIS entities (3DSOLID/BODY/REGION) are native to R2000-R2010; R2013+ uses ACDSDATA binary SAB.".into(),
            });
        }

        Self {
            target_version,
            issues,
        }
    }
}
