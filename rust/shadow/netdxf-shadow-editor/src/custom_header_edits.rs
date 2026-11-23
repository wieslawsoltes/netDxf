#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, IShadowGraphStore, ObjectId, ShadowEditBatch, ShadowGraphPatch, ShadowResult, ShadowSnapshot, ShadowValue, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::sync::Arc;

pub struct CustomHeaderEdits;

impl CustomHeaderEdits {
    pub fn set_property(
        snapshot: &ShadowSnapshot,
        name: impl Into<String>,
        value: impl Into<String>,
    ) -> ShadowGraphPatch {
        let mut edit = netdxf_shadow::ShadowGraphEdit::new(snapshot.clone());
        let _ = edit.set(&snapshot.root, name, ShadowValue::from_string(value));
        edit.build()
    }

    pub fn set(
        snapshot: &ShadowSnapshot,
        name: impl Into<String>,
        value: impl Into<String>,
    ) -> ShadowGraphPatch {
        Self::set_property(snapshot, name, value)
    }

    pub fn remove(snapshot: &ShadowSnapshot, name: impl Into<String>) -> ShadowGraphPatch {
        let mut edit = netdxf_shadow::ShadowGraphEdit::new(snapshot.clone());
        let _ = edit.set(&snapshot.root, name, ShadowValue::null());
        edit.build()
    }
}

