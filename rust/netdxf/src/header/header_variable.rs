use crate::vector3::Vector3;

/// Typed value stored in a custom DXF header variable.
#[derive(Clone, Debug, PartialEq)]
pub enum HeaderVariableValue {
    String(String),
    Int16(i16),
    Int32(i32),
    Float(f64),
    Bool(bool),
    Vector3(Vector3),
}

/// Defines a DXF header variable `(name, group_code, value)`.
#[derive(Clone, Debug, PartialEq)]
pub struct HeaderVariable {
    pub name: String,
    pub group_code: i16,
    pub value: HeaderVariableValue,
}

impl HeaderVariable {
    pub fn new(name: impl Into<String>, group_code: i16, value: HeaderVariableValue) -> Self {
        Self {
            name: name.into(),
            group_code,
            value,
        }
    }
}
