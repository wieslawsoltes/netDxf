use crate::{DraftingCommand, DraftingPlan};
use netdxf_shadow::{
    Affine3, ObjectId, ShadowGraphPatch, ShadowObject, ShadowObjectChange, ShadowObjectKind,
    ShadowSnapshot, ShadowValue, Vec3,
};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BlockCreation {
    pub name: String,
    pub description: String,
    pub origin: Vec3,
    pub base_point: Vec3,
    pub layer: String,
    pub disposition: BlockSourceDisposition,
    pub entities: Vec<ObjectId>,
}

pub struct DraftingBlocks;

pub struct DraftingPlanner;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockSourceDisposition {
    #[default]
    Retain,
    Convert,
    ConvertToInsert,
    Delete,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BlockDefinitionRequest {
    pub name: String,
    pub description: String,
    pub origin: Vec3,
    pub base_point: Vec3,
    pub insert_layer: String,
    pub redefine: bool,
    pub entities: Vec<ObjectId>,
    pub disposition: BlockSourceDisposition,
}

impl DraftingBlocks {
    pub fn list_names(snapshot: &ShadowSnapshot) -> Vec<String> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Block")
            .map(|o| snapshot.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }

    pub fn definition(snapshot: &ShadowSnapshot, name: &str) -> Option<ObjectId> {
        snapshot
            .objects
            .values()
            .find(|o| {
                o.short_type() == "Block"
                    && snapshot
                        .read(o.id.clone())
                        .field("Name")
                        .text()
                        .eq_ignore_ascii_case(name)
            })
            .map(|o| o.id.clone())
    }

    pub fn validate_name(name: &str) -> Result<(), String> {
        if name.trim().is_empty() {
            Err("Block name cannot be empty.".into())
        } else {
            Ok(())
        }
    }

    pub fn validate_reference(snapshot: &ShadowSnapshot, name: &str) -> Result<ObjectId, String> {
        Self::definition(snapshot, name).ok_or_else(|| format!("Unknown block '{name}'."))
    }

    pub fn rename(
        snapshot: &ShadowSnapshot,
        block_id: &ObjectId,
        new_name: impl Into<String>,
    ) -> Result<DraftingPlan, String> {
        DraftingPlanner::rename_block(snapshot, block_id, new_name)
    }
}

impl DraftingPlanner {
    pub fn empty_plan(snapshot: &ShadowSnapshot, name: impl Into<String>) -> DraftingPlan {
        DraftingPlan {
            command: DraftingCommand {
                name: name.into(),
                summary: String::new(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                Vec::new(),
            ),
        }
    }

    /// Plans a drafting command (`LINE`, `CIRCLE`, `ARC`, `POLYLINE`/`PLINE`, `RECTANGLE`/`RECT`,
    /// `POINT`, `TEXT`, `MTEXT`, `MOVE`, `COPY`, `ROTATE`, `SCALE`, `MIRROR`, `DELETE`/`ERASE`,
    /// `OFFSET`) into a deterministic [`ShadowGraphPatch`].
    pub fn plan(snapshot: &ShadowSnapshot, command: &DraftingCommand) -> DraftingPlan {
        let verb = command.name.trim().to_ascii_uppercase();
        let next_seq = snapshot.objects.len() + 1;
        let mut changes = Vec::new();

        let make_entity = |suffix: &str, type_name: &str| -> ShadowObject {
            let id = ObjectId::new(format!("E:DRAFT:{next_seq}:{suffix}"));
            let mut obj = ShadowObject::new(id, type_name);
            obj.kind = ShadowObjectKind::ENTITY;
            if let Some(layer) = &command.layer {
                obj.properties
                    .insert("Layer".into(), ShadowValue::from_string(layer.clone()));
            }
            obj
        };

        match verb.as_str() {
            "LINE" if command.points.len() >= 2 => {
                let mut obj = make_entity("LINE", "netDxf.Entities.Line");
                obj.properties.insert(
                    "StartPoint".into(),
                    ShadowValue::from_vec3(command.points[0], true),
                );
                obj.properties.insert(
                    "EndPoint".into(),
                    ShadowValue::from_vec3(command.points[1], true),
                );
                changes.push(ShadowObjectChange {
                    id: obj.id.clone(),
                    before: None,
                    after: Some(obj),
                });
            }
            "CIRCLE" if !command.points.is_empty() => {
                let center = command.points[0];
                let radius = command
                    .value
                    .or_else(|| command.points.get(1).map(|&p| Vec3::distance(center, p)))
                    .unwrap_or(1.0)
                    .max(1e-9);
                let mut obj = make_entity("CIRCLE", "netDxf.Entities.Circle");
                obj.properties
                    .insert("Center".into(), ShadowValue::from_vec3(center, true));
                obj.properties
                    .insert("Radius".into(), ShadowValue::from_f64(radius));
                changes.push(ShadowObjectChange {
                    id: obj.id.clone(),
                    before: None,
                    after: Some(obj),
                });
            }
            "ARC" if command.points.len() >= 2 => {
                let center = command.points[0];
                let p1 = command.points[1];
                let radius = command
                    .value
                    .unwrap_or_else(|| Vec3::distance(center, p1))
                    .max(1e-9);
                let start_angle = (p1.y - center.y).atan2(p1.x - center.x).to_degrees();
                let end_angle = command
                    .points
                    .get(2)
                    .map(|p2| (p2.y - center.y).atan2(p2.x - center.x).to_degrees())
                    .unwrap_or(start_angle + 90.0);
                let mut obj = make_entity("ARC", "netDxf.Entities.Arc");
                obj.properties
                    .insert("Center".into(), ShadowValue::from_vec3(center, true));
                obj.properties
                    .insert("Radius".into(), ShadowValue::from_f64(radius));
                obj.properties
                    .insert("StartAngle".into(), ShadowValue::from_f64(start_angle));
                obj.properties
                    .insert("EndAngle".into(), ShadowValue::from_f64(end_angle));
                changes.push(ShadowObjectChange {
                    id: obj.id.clone(),
                    before: None,
                    after: Some(obj),
                });
            }
            "POLYLINE" | "PLINE" | "RECTANGLE" | "RECT" if command.points.len() >= 2 => {
                let pts: Vec<Vec3> = if matches!(verb.as_str(), "RECTANGLE" | "RECT")
                    && command.points.len() == 2
                {
                    let a = command.points[0];
                    let b = command.points[1];
                    vec![
                        a,
                        Vec3::new(b.x, a.y, a.z),
                        b,
                        Vec3::new(a.x, b.y, a.z),
                    ]
                } else {
                    command.points.clone()
                };
                let closed = command.closed || matches!(verb.as_str(), "RECTANGLE" | "RECT");
                let mut obj = make_entity("PLINE", "netDxf.Entities.Polyline2D");
                let verts: Vec<ShadowValue> = pts
                    .into_iter()
                    .map(|p| ShadowValue::from_vec3(p, false))
                    .collect();
                obj.properties
                    .insert("Vertexes".into(), ShadowValue::sequence(verts));
                obj.properties
                    .insert("IsClosed".into(), ShadowValue::from_bool(closed));
                changes.push(ShadowObjectChange {
                    id: obj.id.clone(),
                    before: None,
                    after: Some(obj),
                });
            }
            "POINT" if !command.points.is_empty() => {
                let mut obj = make_entity("POINT", "netDxf.Entities.Point");
                obj.properties.insert(
                    "Position".into(),
                    ShadowValue::from_vec3(command.points[0], true),
                );
                changes.push(ShadowObjectChange {
                    id: obj.id.clone(),
                    before: None,
                    after: Some(obj),
                });
            }
            "TEXT" | "MTEXT" if !command.points.is_empty() => {
                let is_mtext = verb == "MTEXT";
                let mut obj = make_entity(
                    if is_mtext { "MTEXT" } else { "TEXT" },
                    if is_mtext {
                        "netDxf.Entities.MText"
                    } else {
                        "netDxf.Entities.Text"
                    },
                );
                obj.properties.insert(
                    "Position".into(),
                    ShadowValue::from_vec3(command.points[0], true),
                );
                obj.properties.insert(
                    "Value".into(),
                    ShadowValue::from_string(command.text.clone().unwrap_or_default()),
                );
                obj.properties.insert(
                    "Height".into(),
                    ShadowValue::from_f64(command.value.unwrap_or(2.5).max(1e-6)),
                );
                if let Some(rot) = command.rotation {
                    obj.properties
                        .insert("Rotation".into(), ShadowValue::from_f64(rot));
                }
                changes.push(ShadowObjectChange {
                    id: obj.id.clone(),
                    before: None,
                    after: Some(obj),
                });
            }
            "DELETE" | "ERASE" => {
                for eid in &command.entities {
                    if let Some(existing) = snapshot.objects.get(eid) {
                        changes.push(ShadowObjectChange {
                            id: eid.clone(),
                            before: Some((**existing).clone()),
                            after: None,
                        });
                    }
                }
            }
            "MOVE" | "COPY" | "ROTATE" | "SCALE" | "MIRROR" => {
                let copy = verb == "COPY" || command.transform.is_some_and(|t| t.copy);
                let base = command
                    .base_point
                    .or_else(|| command.points.first().copied())
                    .unwrap_or(Vec3::ZERO);
                let affine = match verb.as_str() {
                    "MOVE" | "COPY" => {
                        let target = command
                            .points
                            .get(1)
                            .copied()
                            .or_else(|| command.points.first().copied())
                            .unwrap_or(base);
                        let delta = if command.base_point.is_some() {
                            target - base
                        } else if command.points.len() >= 2 {
                            command.points[1] - command.points[0]
                        } else {
                            target
                        };
                        Affine3::move_by(delta)
                    }
                    "ROTATE" => {
                        let angle = command.rotation.or(command.value).unwrap_or(0.0).to_radians();
                        Affine3::move_by(base)
                            * Affine3::rotate_z(angle)
                            * Affine3::move_by(base * -1.0)
                    }
                    "SCALE" => {
                        let s = command.scale.or(command.value).unwrap_or(1.0);
                        Affine3::move_by(base)
                            * Affine3::scale(Vec3::new(s, s, s))
                            * Affine3::move_by(base * -1.0)
                    }
                    "MIRROR" => {
                        let p1 = command.points.first().copied().unwrap_or(base);
                        let p2 = command
                            .points
                            .get(1)
                            .copied()
                            .unwrap_or(p1 + Vec3::UNIT_X);
                        let axis = (p2 - p1).normalized();
                        let ang = axis.y.atan2(axis.x);
                        Affine3::move_by(p1)
                            * Affine3::rotate_z(ang)
                            * Affine3::scale(Vec3::new(1.0, -1.0, 1.0))
                            * Affine3::rotate_z(-ang)
                            * Affine3::move_by(p1 * -1.0)
                    }
                    _ => Affine3::IDENTITY,
                };

                for (idx, eid) in command.entities.iter().enumerate() {
                    let Some(existing) = snapshot.objects.get(eid) else {
                        continue;
                    };
                    let reader = snapshot.read(eid.clone());
                    let mut updated = (**existing).clone();
                    for prop in ["StartPoint", "EndPoint", "Center", "Position"] {
                        let f = reader.field(prop);
                        if f.exists() {
                            let p = affine.point(f.vector(Vec3::ZERO));
                            updated
                                .properties
                                .insert(prop.into(), ShadowValue::from_vec3(p, true));
                        }
                    }
                    if verb == "SCALE" {
                        if let Some(s) = command.scale.or(command.value) {
                            if reader.field("Radius").exists() {
                                let r = reader.field("Radius").number(0.0) * s.abs();
                                updated
                                    .properties
                                    .insert("Radius".into(), ShadowValue::from_f64(r));
                            }
                        }
                    }
                    if copy {
                        let new_id = ObjectId::new(format!("E:COPY:{next_seq}:{idx}"));
                        updated.id = new_id.clone();
                        changes.push(ShadowObjectChange {
                            id: new_id,
                            before: None,
                            after: Some(updated),
                        });
                    } else {
                        changes.push(ShadowObjectChange {
                            id: eid.clone(),
                            before: Some((**existing).clone()),
                            after: Some(updated),
                        });
                    }
                }
            }
            "OFFSET" => {
                let dist = command.value.unwrap_or(1.0);
                for (idx, eid) in command.entities.iter().enumerate() {
                    let Some(existing) = snapshot.objects.get(eid) else {
                        continue;
                    };
                    let reader = snapshot.read(eid.clone());
                    let mut cloned = (**existing).clone();
                    let new_id = ObjectId::new(format!("E:OFFSET:{next_seq}:{idx}"));
                    cloned.id = new_id.clone();
                    match reader.type_name() {
                        "Line" => {
                            let a = reader.field("StartPoint").vector(Vec3::ZERO);
                            let b = reader.field("EndPoint").vector(Vec3::ZERO);
                            let dir = (b - a).normalized();
                            let perp = Vec3::new(-dir.y, dir.x, 0.0) * dist;
                            cloned.properties.insert(
                                "StartPoint".into(),
                                ShadowValue::from_vec3(a + perp, true),
                            );
                            cloned.properties.insert(
                                "EndPoint".into(),
                                ShadowValue::from_vec3(b + perp, true),
                            );
                        }
                        "Circle" | "Arc" => {
                            let r = (reader.field("Radius").number(0.0) + dist).max(1e-9);
                            cloned
                                .properties
                                .insert("Radius".into(), ShadowValue::from_f64(r));
                        }
                        _ => {}
                    }
                    changes.push(ShadowObjectChange {
                        id: new_id,
                        before: None,
                        after: Some(cloned),
                    });
                }
            }
            _ => {}
        }

        DraftingPlan {
            command: command.clone(),
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                changes,
            ),
        }
    }

    pub fn plan_in_block(
        snapshot: &ShadowSnapshot,
        _block_name: &str,
        command: &DraftingCommand,
    ) -> DraftingPlan {
        Self::plan(snapshot, command)
    }

    pub fn create_block(snapshot: &ShadowSnapshot, request: &BlockCreation) -> DraftingPlan {
        let id = ObjectId::new(format!("O:Block:{}", request.name));
        let mut obj = ShadowObject::new(id.clone(), "netDxf.Blocks.Block");
        obj.properties.insert(
            "Name".into(),
            ShadowValue::from_string(request.name.clone()),
        );
        obj.properties.insert(
            "Origin".into(),
            ShadowValue::from_vec3(request.origin, false),
        );
        let refs = request
            .entities
            .iter()
            .cloned()
            .map(ShadowValue::reference)
            .collect();
        obj.properties
            .insert("Entities".into(), ShadowValue::sequence(refs));
        DraftingPlan {
            command: DraftingCommand {
                name: "CREATE BLOCK".into(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                vec![ShadowObjectChange {
                    id,
                    before: None,
                    after: Some(obj),
                }],
            ),
        }
    }

    pub fn create_block_in_definition(
        snapshot: &ShadowSnapshot,
        _owner_block: &str,
        request: &BlockCreation,
    ) -> DraftingPlan {
        Self::create_block(snapshot, request)
    }

    pub fn plan_block_definition(
        snapshot: &ShadowSnapshot,
        request: &BlockDefinitionRequest,
    ) -> DraftingPlan {
        Self::create_block(
            snapshot,
            &BlockCreation {
                name: request.name.clone(),
                origin: request.origin,
                entities: request.entities.clone(),
                ..Default::default()
            },
        )
    }

    pub fn plan_block_definition_in_definition(
        snapshot: &ShadowSnapshot,
        owner_block: &str,
        request: &BlockDefinitionRequest,
    ) -> DraftingPlan {
        Self::create_block_in_definition(
            snapshot,
            owner_block,
            &BlockCreation {
                name: request.name.clone(),
                origin: request.origin,
                entities: request.entities.clone(),
                ..Default::default()
            },
        )
    }

    pub fn rename_block(
        snapshot: &ShadowSnapshot,
        block_id: &ObjectId,
        new_name: impl Into<String>,
    ) -> Result<DraftingPlan, String> {
        let existing = snapshot
            .objects
            .get(block_id)
            .ok_or_else(|| "Unknown block.".to_string())?;
        let mut updated = (**existing).clone();
        updated
            .properties
            .insert("Name".into(), ShadowValue::from_string(new_name.into()));
        Ok(DraftingPlan {
            command: DraftingCommand {
                name: "RENAME BLOCK".into(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                vec![ShadowObjectChange {
                    id: block_id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                }],
            ),
        })
    }
}
