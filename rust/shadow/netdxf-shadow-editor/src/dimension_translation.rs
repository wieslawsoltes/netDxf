use netdxf_shadow::{Affine3, ObjectId, ShadowPropertyEdit, ShadowReader, ShadowValue, Vec3};

/// Translates DXF dimension entities in their object coordinate system (OCS) and clears any cached
/// anonymous block reference so downstream projectors regenerate it (`DimensionTranslation.cs`).
pub struct DimensionTranslation;

impl DimensionTranslation {
    pub fn is_dimension_type(type_name: &str) -> bool {
        matches!(
            type_name,
            "LinearDimension"
                | "AlignedDimension"
                | "RadialDimension"
                | "DiametricDimension"
                | "Angular2LineDimension"
                | "Angular3PointDimension"
                | "OrdinateDimension"
                | "ArcLengthDimension"
                | "Dimension"
        )
    }

    /// Appends property edits translating `entity` by world-space `delta`.
    /// Returns `true` if `entity` was recognized as a dimension type.
    pub fn translate(
        entity: &ShadowReader<'_>,
        delta: Vec3,
        edits: &mut Vec<ShadowPropertyEdit>,
    ) -> bool {
        let ty = entity.type_name();
        if !Self::is_dimension_type(ty) {
            return false;
        }
        let Some(id) = entity.id().cloned() else {
            return false;
        };

        let normal = entity.field("Normal").vector(Vec3::UNIT_Z);
        let basis = Affine3::object_coordinates(if normal.length() > 0.0 {
            normal
        } else {
            Vec3::UNIT_Z
        });
        let local = Vec3::new(
            Vec3::dot(delta, basis.x),
            Vec3::dot(delta, basis.y),
            Vec3::dot(delta, basis.z),
        );
        let local_xy = Vec3::new(local.x, local.y, 0.0);

        let point_props: &[&str] = match ty {
            "LinearDimension" | "AlignedDimension" => {
                &["FirstReferencePoint", "SecondReferencePoint", "DefinitionPoint"]
            }
            "RadialDimension" | "DiametricDimension" => &["CenterPoint", "ReferencePoint"],
            "Angular2LineDimension" => &[
                "StartFirstLine",
                "EndFirstLine",
                "StartSecondLine",
                "EndSecondLine",
            ],
            "Angular3PointDimension" => &["CenterPoint", "StartPoint", "EndPoint"],
            "OrdinateDimension" => &["FeaturePoint", "LeaderEndPoint"],
            "ArcLengthDimension" => {
                &["FirstReferencePoint", "SecondReferencePoint", "DefinitionPoint"]
            }
            _ => &[
                "FirstReferencePoint",
                "SecondReferencePoint",
                "DefinitionPoint",
                "CenterPoint",
                "ReferencePoint",
            ],
        };

        for prop in point_props {
            let f = entity.field(prop);
            if f.exists() {
                let before = f.vector(Vec3::ZERO);
                let after = before + local_xy;
                let has_z = matches!(&f.value, ShadowValue::Structure(s) if s.fields.contains_key("Z"));
                edits.push(ShadowPropertyEdit {
                    object: id.clone(),
                    property: (*prop).to_string(),
                    before: f.value.clone(),
                    after: ShadowValue::from_vec3(after, has_z),
                });
            }
        }

        // Translate TextReferencePoint if explicitly positioned
        let trp = entity.field("TextReferencePoint");
        if trp.exists() {
            let before = trp.vector(Vec3::ZERO);
            let after = before + local_xy;
            let has_z = matches!(&trp.value, ShadowValue::Structure(s) if s.fields.contains_key("Z"));
            edits.push(ShadowPropertyEdit {
                object: id.clone(),
                property: "TextReferencePoint".into(),
                before: trp.value.clone(),
                after: ShadowValue::from_vec3(after, has_z),
            });
        }

        // Adjust OCS elevation if delta has a component along Normal
        if local.z.abs() > 0.0 {
            let elev = entity.field("Elevation");
            let before_val = elev.number(0.0);
            edits.push(ShadowPropertyEdit {
                object: id.clone(),
                property: "Elevation".into(),
                before: elev.value.clone(),
                after: ShadowValue::from_f64(before_val + local.z),
            });
        }

        // Invalidate cached dimension block so projectors regenerate geometry
        let blk = entity.field("Block");
        if blk.exists() && !matches!(blk.value, ShadowValue::Null(_)) {
            edits.push(ShadowPropertyEdit {
                object: id,
                property: "Block".into(),
                before: blk.value.clone(),
                after: ShadowValue::null(),
            });
        }

        true
    }
}

/// Helper to build a single-dimension translation edit list.
pub fn translate_dimension_entity(
    entity: &ShadowReader<'_>,
    delta: Vec3,
) -> (Option<ObjectId>, Vec<ShadowPropertyEdit>) {
    let mut edits = Vec::new();
    if DimensionTranslation::translate(entity, delta, &mut edits) {
        (entity.id().cloned(), edits)
    } else {
        (None, edits)
    }
}
