use crate::shadow_capture::ShadowCaptureOptions;
use netdxf::entities::EntityObject;
use netdxf::{DxfDocument, Vector3};
use netdxf_shadow::{
    IShadowGraphStore, IShadowStore, ObjectId, ShadowEditBatch, ShadowGraphPatch,
    ShadowMemoryStore, ShadowObject, ShadowObjectKind, ShadowResult, ShadowSnapshot, ShadowValue,
    Vec3,
};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::sync::Arc;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ShadowSourceValidationMode {
    #[default]
    Strict,
    Permissive,
}

fn to_v3(v: Vector3) -> Vec3 {
    Vec3::new(v.x, v.y, v.z)
}

/// Live `netdxf::DxfDocument` session backed by an immutable [`ShadowSnapshot`] store.
pub struct ShadowSession {
    store: ShadowMemoryStore,
    pub validation_mode: ShadowSourceValidationMode,
    pub capture_options: ShadowCaptureOptions,
}

impl ShadowSession {
    pub fn from_document(
        doc: &DxfDocument,
        document_id: impl Into<String>,
        options: ShadowCaptureOptions,
    ) -> Self {
        let snapshot = Self::capture(doc, document_id, options);
        Self {
            store: ShadowMemoryStore::new(snapshot),
            validation_mode: ShadowSourceValidationMode::Strict,
            capture_options: options,
        }
    }

    /// Captures entities, layers, blocks, and document metadata from `doc` into an immutable [`ShadowSnapshot`].
    pub fn capture(
        doc: &DxfDocument,
        document_id: impl Into<String>,
        options: ShadowCaptureOptions,
    ) -> ShadowSnapshot {
        let root_id = ObjectId::new("O:Document");
        let mut objects: BTreeMap<ObjectId, Arc<ShadowObject>> = BTreeMap::new();
        let mut entity_ids: BTreeSet<ObjectId> = BTreeSet::new();

        // Capture layers
        let mut layer_refs = Vec::new();
        for layer in doc.layers.iter() {
            if objects.len() >= options.maximum_objects {
                break;
            }
            let id = ObjectId::new(format!("O:Layer:{}", layer.table.name));
            let mut props = BTreeMap::new();
            props.insert(
                "Name".into(),
                ShadowValue::from_string(layer.table.name.clone()),
            );
            props.insert("IsVisible".into(), ShadowValue::from_bool(layer.is_visible));
            props.insert("IsFrozen".into(), ShadowValue::from_bool(layer.is_frozen));
            props.insert("IsLocked".into(), ShadowValue::from_bool(layer.is_locked));
            let [r, g, b] = layer.color.to_rgb();
            let argb = 0xFF00_0000u32 | ((r as u32) << 16) | ((g as u32) << 8) | (b as u32);
            props.insert("ColorArgb".into(), ShadowValue::from_f64(argb as f64));
            let writable: BTreeSet<String> = props.keys().cloned().collect();
            layer_refs.push(ShadowValue::reference(id.clone()));
            objects.insert(
                id.clone(),
                Arc::new(ShadowObject {
                    id,
                    type_name: "netDxf.Tables.Layer".into(),
                    kind: ShadowObjectKind::TABLE,
                    properties: props,
                    writable_properties: writable,
                }),
            );
        }

        // Capture blocks
        let mut block_refs = Vec::new();
        for block in doc.blocks.iter() {
            if objects.len() >= options.maximum_objects {
                break;
            }
            let block_id = ObjectId::new(format!("O:Block:{}", block.table.name));
            let mut blk_entity_refs = Vec::new();
            for (bidx, bent) in block.entities.iter().enumerate() {
                if objects.len() >= options.maximum_objects {
                    break;
                }
                let eid = ObjectId::new(format!("O:BlockEntity:{}:{bidx}", block.table.name));
                let obj = Self::capture_entity(eid.clone(), bent);
                blk_entity_refs.push(ShadowValue::reference(eid.clone()));
                objects.insert(eid, Arc::new(obj));
            }
            let mut props = BTreeMap::new();
            props.insert(
                "Name".into(),
                ShadowValue::from_string(block.table.name.clone()),
            );
            props.insert(
                "Origin".into(),
                ShadowValue::from_vec3(to_v3(block.origin), false),
            );
            props.insert("Entities".into(), ShadowValue::sequence(blk_entity_refs));
            let writable: BTreeSet<String> = props.keys().cloned().collect();
            block_refs.push(ShadowValue::reference(block_id.clone()));
            objects.insert(
                block_id.clone(),
                Arc::new(ShadowObject {
                    id: block_id,
                    type_name: "netDxf.Blocks.Block".into(),
                    kind: ShadowObjectKind::BLOCK,
                    properties: props,
                    writable_properties: writable,
                }),
            );
        }

        // Capture top-level entities
        let mut model_entity_refs = Vec::new();
        for (idx, entity) in doc.entities.iter().enumerate() {
            if objects.len() >= options.maximum_objects {
                break;
            }
            let id = match entity.common().object.handle.as_deref() {
                Some(h) if !h.is_empty() => ObjectId::new(format!("H:{h}")),
                _ => ObjectId::new(format!("O:Entity:{idx}")),
            };
            let obj = Self::capture_entity(id.clone(), entity);
            entity_ids.insert(id.clone());
            model_entity_refs.push(ShadowValue::reference(id.clone()));
            objects.insert(id, Arc::new(obj));
        }

        // Capture Model layout pointing to top-level entities
        let model_block_id = ObjectId::new("O:Block:*Model_Space");
        if !objects.contains_key(&model_block_id) {
            let mut props = BTreeMap::new();
            props.insert("Name".into(), ShadowValue::from_string("*Model_Space"));
            props.insert("Entities".into(), ShadowValue::sequence(model_entity_refs));
            objects.insert(
                model_block_id.clone(),
                Arc::new(ShadowObject {
                    id: model_block_id.clone(),
                    type_name: "netDxf.Blocks.Block".into(),
                    kind: ShadowObjectKind::BLOCK,
                    properties: props,
                    writable_properties: BTreeSet::new(),
                }),
            );
        }
        let model_layout_id = ObjectId::new("O:Layout:Model");
        let mut layout_props = BTreeMap::new();
        layout_props.insert("Name".into(), ShadowValue::from_string("Model"));
        layout_props.insert(
            "AssociatedBlock".into(),
            ShadowValue::reference(model_block_id),
        );
        objects.insert(
            model_layout_id.clone(),
            Arc::new(ShadowObject {
                id: model_layout_id.clone(),
                type_name: "netDxf.Objects.Layout".into(),
                kind: ShadowObjectKind::LAYOUT,
                properties: layout_props,
                writable_properties: BTreeSet::new(),
            }),
        );

        // Root document node
        let mut root_props = BTreeMap::new();
        root_props.insert(
            "EntityCount".into(),
            ShadowValue::from_f64(entity_ids.len() as f64),
        );
        root_props.insert("Layers".into(), ShadowValue::sequence(layer_refs));
        root_props.insert("Blocks".into(), ShadowValue::sequence(block_refs));
        root_props.insert(
            "Layouts".into(),
            ShadowValue::sequence(vec![ShadowValue::reference(model_layout_id)]),
        );
        objects.insert(
            root_id.clone(),
            Arc::new(ShadowObject {
                id: root_id.clone(),
                type_name: "netDxf.DxfDocument".to_string(),
                kind: ShadowObjectKind::DOCUMENT,
                properties: root_props,
                writable_properties: BTreeSet::new(),
            }),
        );

        ShadowSnapshot::new(document_id, 0, root_id, objects, entity_ids, Vec::new())
    }

    fn capture_entity(id: ObjectId, entity: &EntityObject) -> ShadowObject {
        let mut props = BTreeMap::new();
        let common = entity.common();
        props.insert(
            "Layer".into(),
            ShadowValue::from_string(common.layer.clone()),
        );
        props.insert(
            "IsVisible".into(),
            ShadowValue::from_bool(common.is_visible),
        );
        let [r, g, b] = common.color.to_rgb();
        let argb = 0xFF00_0000u32 | ((r as u32) << 16) | ((g as u32) << 8) | (b as u32);
        props.insert("ColorArgb".into(), ShadowValue::from_f64(argb as f64));
        props.insert(
            "Normal".into(),
            ShadowValue::from_vec3(to_v3(common.normal), false),
        );

        let type_name = match entity {
            EntityObject::Line(l) => {
                props.insert(
                    "StartPoint".into(),
                    ShadowValue::from_vec3(to_v3(l.start_point), false),
                );
                props.insert(
                    "EndPoint".into(),
                    ShadowValue::from_vec3(to_v3(l.end_point), false),
                );
                "netDxf.Entities.Line"
            }
            EntityObject::Circle(c) => {
                props.insert(
                    "Center".into(),
                    ShadowValue::from_vec3(to_v3(c.center), false),
                );
                props.insert("Radius".into(), ShadowValue::from_f64(c.radius));
                "netDxf.Entities.Circle"
            }
            EntityObject::Arc(a) => {
                props.insert(
                    "Center".into(),
                    ShadowValue::from_vec3(to_v3(a.center), false),
                );
                props.insert("Radius".into(), ShadowValue::from_f64(a.radius));
                props.insert("StartAngle".into(), ShadowValue::from_f64(a.start_angle));
                props.insert("EndAngle".into(), ShadowValue::from_f64(a.end_angle));
                "netDxf.Entities.Arc"
            }
            EntityObject::Ellipse(e) => {
                props.insert(
                    "Center".into(),
                    ShadowValue::from_vec3(to_v3(e.center), false),
                );
                props.insert("MajorAxis".into(), ShadowValue::from_f64(e.major_axis));
                props.insert("MinorAxis".into(), ShadowValue::from_f64(e.minor_axis));
                props.insert("Rotation".into(), ShadowValue::from_f64(e.rotation));
                props.insert("StartAngle".into(), ShadowValue::from_f64(e.start_angle));
                props.insert("EndAngle".into(), ShadowValue::from_f64(e.end_angle));
                "netDxf.Entities.Ellipse"
            }
            EntityObject::Point(p) => {
                props.insert(
                    "Position".into(),
                    ShadowValue::from_vec3(to_v3(p.position), false),
                );
                "netDxf.Entities.Point"
            }
            EntityObject::Polyline2D(p) => {
                let verts: Vec<ShadowValue> = p
                    .vertexes
                    .iter()
                    .map(|v| {
                        let mut m = BTreeMap::new();
                        m.insert(
                            "Position".into(),
                            ShadowValue::from_vec3(Vec3::xy(v.position.x, v.position.y), true),
                        );
                        m.insert("Bulge".into(), ShadowValue::from_f64(v.bulge));
                        ShadowValue::Structure(netdxf_shadow::Structure {
                            type_name: "netDxf.Entities.Polyline2DVertex".into(),
                            fields: m,
                        })
                    })
                    .collect();
                props.insert("Vertexes".into(), ShadowValue::sequence(verts));
                props.insert("IsClosed".into(), ShadowValue::from_bool(p.is_closed));
                props.insert("Elevation".into(), ShadowValue::from_f64(p.elevation));
                "netDxf.Entities.Polyline2D"
            }
            EntityObject::Polyline3D(p) => {
                let verts = p
                    .vertexes
                    .iter()
                    .map(|&v| ShadowValue::from_vec3(to_v3(v), false))
                    .collect();
                props.insert("Vertexes".into(), ShadowValue::sequence(verts));
                props.insert("IsClosed".into(), ShadowValue::from_bool(p.is_closed));
                "netDxf.Entities.Polyline3D"
            }
            EntityObject::Text(t) => {
                props.insert("Value".into(), ShadowValue::from_string(t.value.clone()));
                props.insert(
                    "Position".into(),
                    ShadowValue::from_vec3(to_v3(t.position), false),
                );
                props.insert("Height".into(), ShadowValue::from_f64(t.height));
                props.insert("Rotation".into(), ShadowValue::from_f64(t.rotation));
                "netDxf.Entities.Text"
            }
            EntityObject::MText(m) => {
                props.insert("Value".into(), ShadowValue::from_string(m.value.clone()));
                props.insert(
                    "Position".into(),
                    ShadowValue::from_vec3(to_v3(m.position), false),
                );
                props.insert("Height".into(), ShadowValue::from_f64(m.height));
                props.insert("Rotation".into(), ShadowValue::from_f64(m.rotation));
                "netDxf.Entities.MText"
            }
            EntityObject::Insert(ins) => {
                props.insert(
                    "Block".into(),
                    ShadowValue::reference(ObjectId::new(format!("O:Block:{}", ins.block_name))),
                );
                props.insert(
                    "Position".into(),
                    ShadowValue::from_vec3(to_v3(ins.position), false),
                );
                props.insert(
                    "Scale".into(),
                    ShadowValue::from_vec3(to_v3(ins.scale), false),
                );
                props.insert("Rotation".into(), ShadowValue::from_f64(ins.rotation));
                "netDxf.Entities.Insert"
            }
            EntityObject::Solid(s) => {
                props.insert(
                    "FirstVertex".into(),
                    ShadowValue::from_vec3(Vec3::new(s.first_vertex.x, s.first_vertex.y, s.elevation), false),
                );
                props.insert(
                    "SecondVertex".into(),
                    ShadowValue::from_vec3(Vec3::new(s.second_vertex.x, s.second_vertex.y, s.elevation), false),
                );
                props.insert(
                    "ThirdVertex".into(),
                    ShadowValue::from_vec3(Vec3::new(s.third_vertex.x, s.third_vertex.y, s.elevation), false),
                );
                props.insert(
                    "FourthVertex".into(),
                    ShadowValue::from_vec3(Vec3::new(s.fourth_vertex.x, s.fourth_vertex.y, s.elevation), false),
                );
                "netDxf.Entities.Solid"
            }
            EntityObject::Face3D(f) => {
                props.insert(
                    "FirstVertex".into(),
                    ShadowValue::from_vec3(to_v3(f.first_vertex), false),
                );
                props.insert(
                    "SecondVertex".into(),
                    ShadowValue::from_vec3(to_v3(f.second_vertex), false),
                );
                props.insert(
                    "ThirdVertex".into(),
                    ShadowValue::from_vec3(to_v3(f.third_vertex), false),
                );
                props.insert(
                    "FourthVertex".into(),
                    ShadowValue::from_vec3(to_v3(f.fourth_vertex), false),
                );
                "netDxf.Entities.Face3D"
            }
            EntityObject::Spline(_) => "netDxf.Entities.Spline",
            EntityObject::Hatch(_) => "netDxf.Entities.Hatch",
            EntityObject::Dimension(_) => "netDxf.Entities.Dimension",
            _ => "netDxf.Entities.EntityObject",
        };

        let writable: BTreeSet<String> = props.keys().cloned().collect();
        ShadowObject {
            id,
            type_name: type_name.to_string(),
            kind: ShadowObjectKind::ENTITY,
            properties: props,
            writable_properties: writable,
        }
    }

    /// Synchronizes modified entity properties from the current snapshot back into `doc`.
    pub fn write_back(&self, doc: &mut DxfDocument) -> usize {
        let snap = self.current();
        let mut updated = 0usize;
        for (idx, entity) in doc.entities.iter_mut().enumerate() {
            let id = match entity.common().object.handle.as_deref() {
                Some(h) if !h.is_empty() => ObjectId::new(format!("H:{h}")),
                _ => ObjectId::new(format!("O:Entity:{idx}")),
            };
            if !snap.objects.contains_key(&id) {
                continue;
            }
            let r = snap.read(id);
            let layer = r.field("Layer").text().to_string();
            if !layer.is_empty() && entity.common().layer != layer {
                entity.common_mut().layer = layer;
                updated += 1;
            }
            match entity {
                EntityObject::Line(l) => {
                    let s = r.field("StartPoint").vector(to_v3(l.start_point));
                    let e = r.field("EndPoint").vector(to_v3(l.end_point));
                    l.start_point = Vector3::new(s.x, s.y, s.z);
                    l.end_point = Vector3::new(e.x, e.y, e.z);
                }
                EntityObject::Circle(c) => {
                    let ctr = r.field("Center").vector(to_v3(c.center));
                    c.center = Vector3::new(ctr.x, ctr.y, ctr.z);
                    let rad = r.field("Radius").number(c.radius);
                    if rad > 0.0 {
                        c.radius = rad;
                    }
                }
                EntityObject::Text(t) => {
                    let txt = r.field("Value").text().to_string();
                    if !txt.is_empty() {
                        t.value = txt;
                    }
                }
                _ => {}
            }
        }
        updated
    }
}

impl IShadowStore for ShadowSession {
    fn current(&self) -> Arc<ShadowSnapshot> {
        self.store.current()
    }

    fn apply(&self, batch: &ShadowEditBatch) -> ShadowResult<Arc<ShadowSnapshot>> {
        self.store.apply(batch)
    }
}

impl IShadowGraphStore for ShadowSession {
    fn apply_patch(&self, patch: &ShadowGraphPatch) -> ShadowResult<Arc<ShadowSnapshot>> {
        self.store.apply_patch(patch)
    }
}
