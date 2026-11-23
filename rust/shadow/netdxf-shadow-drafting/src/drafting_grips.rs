use crate::{DraftingCommand, DraftingPlan, DraftingPlanner};
use netdxf_shadow::{
    ObjectId, ShadowGraphPatch, ShadowObjectChange, ShadowSnapshot, ShadowValue, Vec3,
};

/// Extracts interactive editing grips from DXF entities and plans grip-move patches
/// (`DraftingGrips.cs`).
pub struct DraftingGrips;

impl DraftingGrips {
    pub fn read(snapshot: &ShadowSnapshot, entity: &ObjectId) -> Vec<crate::DraftingGrip> {
        Self::extract(snapshot, entity)
    }

    pub fn extract(snapshot: &ShadowSnapshot, entity: &ObjectId) -> Vec<crate::DraftingGrip> {
        let r = snapshot.read(entity.clone());
        if !r.exists() {
            return Vec::new();
        }
        let mut grips = Vec::new();
        let mut push_grip = |key: &str, property: &str, kind: &str, position: Vec3| {
            grips.push(crate::DraftingGrip {
                id: format!("{}:{key}", entity.as_str()),
                entity: entity.clone(),
                key: key.to_string(),
                property: property.to_string(),
                kind: kind.to_string(),
                index: grips.len(),
                position,
            });
        };

        match r.type_name() {
            "Line" => {
                let a = r.field("StartPoint").vector(Vec3::ZERO);
                let b = r.field("EndPoint").vector(Vec3::ZERO);
                push_grip("StartPoint", "StartPoint", "Endpoint", a);
                push_grip("Midpoint", "Midpoint", "Midpoint", (a + b) * 0.5);
                push_grip("EndPoint", "EndPoint", "Endpoint", b);
            }
            "Circle" => {
                let c = r.field("Center").vector(Vec3::ZERO);
                let rad = r.field("Radius").number(0.0);
                push_grip("Center", "Center", "Center", c);
                if rad > 0.0 {
                    push_grip("Radius:E", "Radius", "Quadrant", c + Vec3::new(rad, 0.0, 0.0));
                    push_grip("Radius:N", "Radius", "Quadrant", c + Vec3::new(0.0, rad, 0.0));
                    push_grip("Radius:W", "Radius", "Quadrant", c + Vec3::new(-rad, 0.0, 0.0));
                    push_grip("Radius:S", "Radius", "Quadrant", c + Vec3::new(0.0, -rad, 0.0));
                }
            }
            "Arc" => {
                let c = r.field("Center").vector(Vec3::ZERO);
                let rad = r.field("Radius").number(0.0);
                let sa = r.field("StartAngle").number(0.0).to_radians();
                let ea = r.field("EndAngle").number(90.0).to_radians();
                let mut sweep = ea - sa;
                if sweep < 0.0 {
                    sweep += std::f64::consts::TAU;
                }
                let ma = sa + sweep * 0.5;
                push_grip("Center", "Center", "Center", c);
                push_grip("Start", "StartAngle", "Endpoint", c + Vec3::new(rad * sa.cos(), rad * sa.sin(), 0.0));
                push_grip("Mid", "Radius", "Midpoint", c + Vec3::new(rad * ma.cos(), rad * ma.sin(), 0.0));
                push_grip("End", "EndAngle", "Endpoint", c + Vec3::new(rad * ea.cos(), rad * ea.sin(), 0.0));
            }
            "Polyline2D" | "Polyline3D" => {
                for (idx, v) in r.field("Vertexes").items().iter().enumerate() {
                    let pos = if v.field("Position").exists() {
                        v.field("Position").vector(Vec3::ZERO)
                    } else {
                        v.vector(Vec3::ZERO)
                    };
                    push_grip(&format!("Vertex:{idx}"), "Vertexes", "Vertex", pos);
                }
            }
            _ => {
                for key in ["StartPoint", "EndPoint", "Center", "Position"] {
                    let f = r.field(key);
                    if f.exists() {
                        push_grip(key, key, "Point", f.vector(Vec3::ZERO));
                    }
                }
            }
        }
        grips
    }

    pub fn r#move(
        snapshot: &ShadowSnapshot,
        grip: &crate::DraftingGrip,
        target: Vec3,
    ) -> DraftingPlan {
        let Some(existing) = snapshot.objects.get(&grip.entity) else {
            return DraftingPlanner::empty_plan(snapshot, format!("GRIP MOVE {}", grip.key));
        };
        let mut updated = (**existing).clone();
        let reader = snapshot.read(grip.entity.clone());
        let mut changes = Vec::new();

        match grip.property.as_str() {
            "Midpoint" if reader.type_name() == "Line" => {
                let delta = target - grip.position;
                let a = reader.field("StartPoint").vector(Vec3::ZERO) + delta;
                let b = reader.field("EndPoint").vector(Vec3::ZERO) + delta;
                updated
                    .properties
                    .insert("StartPoint".into(), ShadowValue::from_vec3(a, true));
                updated
                    .properties
                    .insert("EndPoint".into(), ShadowValue::from_vec3(b, true));
                changes.push(ShadowObjectChange {
                    id: grip.entity.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
            "Radius" => {
                let c = reader.field("Center").vector(Vec3::ZERO);
                let new_r = Vec3::distance(c, target).max(1e-9);
                updated
                    .properties
                    .insert("Radius".into(), ShadowValue::from_f64(new_r));
                changes.push(ShadowObjectChange {
                    id: grip.entity.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
            "StartAngle" | "EndAngle" => {
                let c = reader.field("Center").vector(Vec3::ZERO);
                let d = target - c;
                let mut deg = d.y.atan2(d.x).to_degrees();
                if deg < 0.0 {
                    deg += 360.0;
                }
                updated
                    .properties
                    .insert(grip.property.clone(), ShadowValue::from_f64(deg));
                changes.push(ShadowObjectChange {
                    id: grip.entity.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
            "Vertexes" => {
                if let Some(idx_str) = grip.key.strip_prefix("Vertex:") {
                    if let Ok(idx) = idx_str.parse::<usize>() {
                        let items = reader.field("Vertexes").items();
                        if let Some(v) = items.get(idx) {
                            if let Some(vid) = v.id() {
                                if let Some(v_obj) = snapshot.objects.get(vid) {
                                    let mut v_upd = (**v_obj).clone();
                                    v_upd.properties.insert(
                                        "Position".into(),
                                        ShadowValue::from_vec3(target, false),
                                    );
                                    changes.push(ShadowObjectChange {
                                        id: vid.clone(),
                                        before: Some((**v_obj).clone()),
                                        after: Some(v_upd),
                                    });
                                }
                            } else {
                                let mut seq: Vec<ShadowValue> =
                                    items.iter().map(|it| it.value.clone()).collect();
                                seq[idx] = ShadowValue::from_vec3(target, true);
                                updated
                                    .properties
                                    .insert("Vertexes".into(), ShadowValue::sequence(seq));
                                changes.push(ShadowObjectChange {
                                    id: grip.entity.clone(),
                                    before: Some((**existing).clone()),
                                    after: Some(updated),
                                });
                            }
                        }
                    }
                }
            }
            prop => {
                updated
                    .properties
                    .insert(prop.to_string(), ShadowValue::from_vec3(target, true));
                changes.push(ShadowObjectChange {
                    id: grip.entity.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
        }

        DraftingPlan {
            command: DraftingCommand {
                name: format!("GRIP MOVE {}", grip.key),
                entities: vec![grip.entity.clone()],
                points: vec![grip.position, target],
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                changes,
            ),
        }
    }
}
