use crate::DimensionTranslation;
use netdxf_shadow::{
    Affine3, ObjectId, ShadowEditBatch, ShadowPropertyEdit, ShadowReader, ShadowSnapshot,
    ShadowValue, Vec3,
};
use std::collections::BTreeSet;

/// Translates DXF entities in a detached [`ShadowSnapshot`] with full per-entity-type semantics
/// (`EditOperations.cs`).
pub struct EditOperations;

impl EditOperations {
    pub fn translate(
        snapshot: &ShadowSnapshot,
        entities: &[ObjectId],
        delta: Vec3,
    ) -> ShadowEditBatch {
        Self::move_entities(snapshot, entities, delta)
    }

    pub fn move_entities(
        snapshot: &ShadowSnapshot,
        entities: &[ObjectId],
        delta: Vec3,
    ) -> ShadowEditBatch {
        let mut edits = Vec::new();
        let mut visited: BTreeSet<ObjectId> = BTreeSet::new();
        for id in entities {
            Self::translate_one(snapshot, id, delta, &mut visited, &mut edits);
        }
        ShadowEditBatch::create(snapshot, edits)
    }

    fn translate_one(
        snapshot: &ShadowSnapshot,
        id: &ObjectId,
        delta: Vec3,
        visited: &mut BTreeSet<ObjectId>,
        edits: &mut Vec<ShadowPropertyEdit>,
    ) {
        if !visited.insert(id.clone()) {
            return;
        }
        let entity = snapshot.read(id.clone());
        if !entity.exists() {
            return;
        }

        if DimensionTranslation::translate(&entity, delta, edits) {
            return;
        }

        match entity.type_name() {
            "Line" => {
                shift_point(&entity, "StartPoint", delta, edits);
                shift_point(&entity, "EndPoint", delta, edits);
            }
            "Viewport" => {
                shift_point(&entity, "Center", delta, edits);
                if let Some(clip_id) = entity.field("ClippingBoundary").id() {
                    Self::translate_one(snapshot, clip_id, delta, visited, edits);
                }
            }
            "Circle" | "Arc" | "Ellipse" => {
                shift_point(&entity, "Center", delta, edits);
            }
            "Text" | "MText" | "Point" | "Image" | "Attribute" | "AttributeDefinition" => {
                shift_point(&entity, "Position", delta, edits);
            }
            "Insert" => {
                shift_point(&entity, "Position", delta, edits);
                for attr in entity.field("Attributes").items() {
                    if let Some(aid) = attr.id() {
                        Self::translate_one(snapshot, aid, delta, visited, edits);
                    }
                }
            }
            "Polyline2D" => {
                let local = ocs_delta(&entity, delta);
                let local_xy = Vec3::new(local.x, local.y, 0.0);
                for v in entity.field("Vertexes").items() {
                    if v.id().is_some() {
                        shift_point(&v, "Position", local_xy, edits);
                    }
                }
                if local.z.abs() > 0.0 {
                    shift_number(&entity, "Elevation", local.z, edits);
                }
            }
            "Polyline3D" | "Leader" | "Mesh" | "PolyfaceMesh" => {
                shift_sequence_points(&entity, "Vertexes", delta, edits);
            }
            "Spline" => {
                shift_sequence_points(&entity, "ControlPoints", delta, edits);
                if entity.field("FitPoints").exists() {
                    shift_sequence_points(&entity, "FitPoints", delta, edits);
                }
            }
            "Face3D" => {
                for prop in ["FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex"] {
                    shift_point(&entity, prop, delta, edits);
                }
            }
            "Solid" | "Trace" => {
                let local = ocs_delta(&entity, delta);
                let local_xy = Vec3::new(local.x, local.y, 0.0);
                for prop in ["FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex"] {
                    shift_point(&entity, prop, local_xy, edits);
                }
                if local.z.abs() > 0.0 {
                    shift_number(&entity, "Elevation", local.z, edits);
                }
            }
            _ => {
                for prop in ["StartPoint", "EndPoint", "Center", "Position"] {
                    shift_point(&entity, prop, delta, edits);
                }
            }
        }
    }
}

fn ocs_delta(entity: &ShadowReader<'_>, world_delta: Vec3) -> Vec3 {
    let normal = entity.field("Normal").vector(Vec3::UNIT_Z);
    let basis = Affine3::object_coordinates(if normal.length() > 0.0 {
        normal
    } else {
        Vec3::UNIT_Z
    });
    Vec3::new(
        Vec3::dot(world_delta, basis.x),
        Vec3::dot(world_delta, basis.y),
        Vec3::dot(world_delta, basis.z),
    )
}

fn shift_point(
    entity: &ShadowReader<'_>,
    prop: &str,
    delta: Vec3,
    edits: &mut Vec<ShadowPropertyEdit>,
) {
    let Some(id) = entity.id().cloned() else {
        return;
    };
    let f = entity.field(prop);
    if !f.exists() {
        return;
    }
    let before = f.vector(Vec3::ZERO);
    let after = before + delta;
    let has_z =
        matches!(&f.value, ShadowValue::Structure(s) if s.fields.contains_key("Z"));
    edits.push(ShadowPropertyEdit {
        object: id,
        property: prop.to_string(),
        before: f.value.clone(),
        after: ShadowValue::from_vec3(after, has_z),
    });
}

fn shift_number(
    entity: &ShadowReader<'_>,
    prop: &str,
    delta: f64,
    edits: &mut Vec<ShadowPropertyEdit>,
) {
    let Some(id) = entity.id().cloned() else {
        return;
    };
    let f = entity.field(prop);
    let before_val = f.number(0.0);
    edits.push(ShadowPropertyEdit {
        object: id,
        property: prop.to_string(),
        before: f.value.clone(),
        after: ShadowValue::from_f64(before_val + delta),
    });
}

fn shift_sequence_points(
    entity: &ShadowReader<'_>,
    prop: &str,
    delta: Vec3,
    edits: &mut Vec<ShadowPropertyEdit>,
) {
    let Some(id) = entity.id().cloned() else {
        return;
    };
    let field = entity.field(prop);
    let items = field.items();
    if items.is_empty() {
        return;
    }
    let mut has_inline = false;
    let mut new_seq = Vec::with_capacity(items.len());
    for item in &items {
        if item.id().is_some() {
            shift_point(item, "Position", delta, edits);
            new_seq.push(item.value.clone());
        } else {
            has_inline = true;
            let before = item.vector(Vec3::ZERO);
            let has_z = matches!(&item.value, ShadowValue::Structure(s) if s.fields.contains_key("Z"));
            new_seq.push(ShadowValue::from_vec3(before + delta, has_z));
        }
    }
    if has_inline {
        edits.push(ShadowPropertyEdit {
            object: id,
            property: prop.to_string(),
            before: field.value.clone(),
            after: ShadowValue::Sequence(netdxf_shadow::Sequence { items: new_seq }),
        });
    }
}
