use crate::math::Vec3;
use bitflags::bitflags;
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::fmt;

/// Session-stable identity. DXF handles use `H:`, other source objects use `O:`.
#[derive(Debug, Clone, PartialEq, Eq, PartialOrd, Ord, Hash, Default, Serialize, Deserialize)]
pub struct ObjectId(pub String);

impl ObjectId {
    pub fn new(value: impl Into<String>) -> Self {
        Self(value.into())
    }

    pub fn value(&self) -> &str {
        &self.0
    }

    pub fn as_str(&self) -> &str {
        &self.0
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

impl fmt::Display for ObjectId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

/// Explicit null variant payload for [`ShadowValue`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub struct NullValue;

/// Invariant-text atomic value preserving full numeric/string precision.
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct Atom {
    pub type_name: String,
    pub text: String,
}

/// Reference to another object node by [`ObjectId`].
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct Reference {
    pub id: ObjectId,
}

/// Ordered sequence of [`ShadowValue`] items.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Sequence {
    pub items: Vec<ShadowValue>,
}

/// Typed key-value structure of [`ShadowValue`] fields.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Structure {
    pub type_name: String,
    pub fields: BTreeMap<String, ShadowValue>,
}

/// Fault value indicating a read/evaluation error.
#[derive(Debug, Clone, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct Fault {
    pub message: String,
}

/// A closed, detached value algebra. No mutable source or graphics object can occur here.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub enum ShadowValue {
    Null(NullValue),
    Atom(Atom),
    Reference(Reference),
    Sequence(Sequence),
    Structure(Structure),
    Fault(Fault),
}

impl Default for ShadowValue {
    fn default() -> Self {
        Self::null()
    }
}

impl ShadowValue {
    pub const fn null() -> Self {
        Self::Null(NullValue)
    }

    pub fn from(value: impl Into<String>) -> Self {
        Self::from_string(value)
    }

    pub fn from_string(value: impl Into<String>) -> Self {
        Self::Atom(Atom {
            type_name: "System.String".to_string(),
            text: value.into(),
        })
    }

    pub fn from_f64(value: f64) -> Self {
        Self::Atom(Atom {
            type_name: "System.Double".to_string(),
            text: format!("{value}"),
        })
    }

    pub fn from_bool(value: bool) -> Self {
        Self::Atom(Atom {
            type_name: "System.Boolean".to_string(),
            text: if value { "True" } else { "False" }.to_string(),
        })
    }

    pub fn from_vec3(value: Vec3, two_dimensional: bool) -> Self {
        let mut fields = BTreeMap::new();
        fields.insert("X".to_string(), Self::from_f64(value.x));
        fields.insert("Y".to_string(), Self::from_f64(value.y));
        if !two_dimensional {
            fields.insert("Z".to_string(), Self::from_f64(value.z));
        }
        Self::Structure(Structure {
            type_name: if two_dimensional {
                "netDxf.Vector2"
            } else {
                "netDxf.Vector3"
            }
            .to_string(),
            fields,
        })
    }

    pub fn reference(id: ObjectId) -> Self {
        Self::Reference(Reference { id })
    }

    pub fn sequence(items: Vec<ShadowValue>) -> Self {
        Self::Sequence(Sequence { items })
    }

    pub fn equivalent(left: &Self, right: &Self) -> bool {
        left == right
    }

    pub fn equivalent_maps(
        left: &BTreeMap<String, ShadowValue>,
        right: &BTreeMap<String, ShadowValue>,
    ) -> bool {
        left == right
    }

    /// Collects all [`ObjectId`] references contained anywhere inside this value.
    pub fn references(&self) -> Vec<ObjectId> {
        let mut out = Vec::new();
        let mut stack = vec![self];
        while let Some(cur) = stack.pop() {
            match cur {
                Self::Reference(r) => out.push(r.id.clone()),
                Self::Sequence(s) => {
                    for item in &s.items {
                        stack.push(item);
                    }
                }
                Self::Structure(st) => {
                    for v in st.fields.values() {
                        stack.push(v);
                    }
                }
                Self::Null(_) | Self::Atom(_) | Self::Fault(_) => {}
            }
        }
        out
    }
}

bitflags! {
    /// Classification flags for a [`ShadowObject`].
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
    pub struct ShadowObjectKind: u32 {
        const OBJECT = 0;
        const ENTITY = 1;
        const TABLE = 2;
        const BLOCK = 4;
        const LAYOUT = 8;
        const DOCUMENT = 16;
    }
}

/// Severity level for [`ShadowDiagnostic`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DiagnosticSeverity {
    Information,
    #[default]
    Warning,
    Error,
}

/// Diagnostic emitted during shadow snapshot capture or validation.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowDiagnostic {
    pub code: String,
    pub message: String,
    pub object: Option<ObjectId>,
    pub severity: DiagnosticSeverity,
}

impl ShadowDiagnostic {
    pub fn new(
        code: impl Into<String>,
        message: impl Into<String>,
        object: Option<ObjectId>,
        severity: DiagnosticSeverity,
    ) -> Self {
        Self {
            code: code.into(),
            message: message.into(),
            object,
            severity,
        }
    }
}

/// Immutable node in a [`ShadowSnapshot`](crate::ShadowSnapshot).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowObject {
    pub id: ObjectId,
    pub type_name: String,
    pub kind: ShadowObjectKind,
    pub properties: BTreeMap<String, ShadowValue>,
    pub writable_properties: BTreeSet<String>,
}

impl ShadowObject {
    pub fn new(id: ObjectId, type_name: impl Into<String>) -> Self {
        Self {
            id,
            type_name: type_name.into(),
            kind: ShadowObjectKind::default(),
            properties: BTreeMap::new(),
            writable_properties: BTreeSet::new(),
        }
    }

    pub fn short_type(&self) -> &str {
        match self.type_name.rfind('.') {
            Some(idx) => &self.type_name[idx + 1..],
            None => &self.type_name,
        }
    }

    pub fn r#type(&self) -> &str {
        &self.type_name
    }

    pub fn get(&self, property: &str) -> ShadowValue {
        self.properties
            .get(property)
            .cloned()
            .unwrap_or_else(ShadowValue::null)
    }
}
