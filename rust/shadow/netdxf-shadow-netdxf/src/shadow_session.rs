use crate::shadow_capture::ShadowCaptureOptions;
use netdxf::entities::EntityObject;
use netdxf::DxfDocument;
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

    pub fn capture(
        doc: &DxfDocument,
        document_id: impl Into<String>,
        options: ShadowCaptureOptions,
    ) -> ShadowSnapshot {
        let root_id = ObjectId::new("O:Document");
        let mut objects: BTreeMap<ObjectId, Arc<ShadowObject>> = BTreeMap::new();
        let mut entity_ids: BTreeSet<ObjectId> = BTreeSet::new();

        for (idx, entity) in doc.entities.iter().enumerate() {
            if objects.len() >= options.maximum_objects {
                break;
            }
            let id = match entity.common().object.handle.as_deref() {
                Some(h) if !h.is_empty() => ObjectId::new(format!("H:{h}")),
                _ => ObjectId::new(format!("O:Entity:{idx}")),
            };
            let mut props = BTreeMap::new();
            props.insert(
                "Layer".into(),
                ShadowValue::from_string(entity.common().layer.clone()),
            );
            let type_name = match entity {
                EntityObject::Line(l) => {
                    props.insert(
                        "StartPoint".into(),
                        ShadowValue::from_vec3(
                            Vec3::new(l.start_point.x, l.start_point.y, l.start_point.z),
                            false,
                        ),
                    );
                    props.insert(
                        "EndPoint".into(),
                        ShadowValue::from_vec3(
                            Vec3::new(l.end_point.x, l.end_point.y, l.end_point.z),
                            false,
                        ),
                    );
                    "netDxf.Entities.Line"
                }
                EntityObject::Circle(c) => {
                    props.insert(
                        "Center".into(),
                        ShadowValue::from_vec3(
                            Vec3::new(c.center.x, c.center.y, c.center.z),
                            false,
                        ),
                    );
                    props.insert("Radius".into(), ShadowValue::from_f64(c.radius));
                    "netDxf.Entities.Circle"
                }
                EntityObject::Point(p) => {
                    props.insert(
                        "Position".into(),
                        ShadowValue::from_vec3(
                            Vec3::new(p.position.x, p.position.y, p.position.z),
                            false,
                        ),
                    );
                    "netDxf.Entities.Point"
                }
                _ => "netDxf.Entities.EntityObject",
            };
            let writable: BTreeSet<String> = props.keys().cloned().collect();
            entity_ids.insert(id.clone());
            objects.insert(
                id.clone(),
                Arc::new(ShadowObject {
                    id,
                    type_name: type_name.to_string(),
                    kind: ShadowObjectKind::ENTITY,
                    properties: props,
                    writable_properties: writable,
                }),
            );
        }

        let mut root_props = BTreeMap::new();
        root_props.insert(
            "EntityCount".into(),
            ShadowValue::from_f64(entity_ids.len() as f64),
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
