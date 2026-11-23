use crate::x_data_code::XDataCode;

/// Typed value stored in an `XDataRecord`.
#[derive(Clone, Debug, PartialEq)]
pub enum XDataValue {
    String(String),
    Binary(Vec<u8>),
    Real(f64),
    Int16(i16),
    Int32(i32),
}

/// An individual DXF extended data record `(code, value)`.
#[derive(Clone, Debug, PartialEq)]
pub struct XDataRecord {
    pub code: XDataCode,
    pub value: XDataValue,
}

impl XDataRecord {
    pub fn new(code: XDataCode, value: XDataValue) -> Self {
        Self { code, value }
    }

    pub fn open_control_string() -> Self {
        Self::new(XDataCode::ControlString, XDataValue::String("{".into()))
    }

    pub fn close_control_string() -> Self {
        Self::new(XDataCode::ControlString, XDataValue::String("}".into()))
    }

    pub fn string(value: impl Into<String>) -> Self {
        Self::new(XDataCode::String, XDataValue::String(value.into()))
    }

    pub fn real(value: f64) -> Self {
        Self::new(XDataCode::Real, XDataValue::Real(value))
    }

    pub fn int16(value: i16) -> Self {
        Self::new(XDataCode::Int16, XDataValue::Int16(value))
    }

    pub fn int32(value: i32) -> Self {
        Self::new(XDataCode::Int32, XDataValue::Int32(value))
    }
}
