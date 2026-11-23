use crate::io::code_value_reader::CodeValue;

/// Immutable `(group_code, value)` DXF tag (`netDxf.IO.DxfTag`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfTag {
    pub code: i16,
    pub value: CodeValue,
}

impl DxfTag {
    #[inline]
    pub fn new(code: i16, value: CodeValue) -> Self {
        Self { code, value }
    }

    #[inline]
    pub fn string(code: i16, value: impl Into<String>) -> Self {
        Self {
            code,
            value: CodeValue::String(value.into()),
        }
    }

    #[inline]
    pub const fn double(code: i16, value: f64) -> Self {
        Self {
            code,
            value: CodeValue::Double(value),
        }
    }

    #[inline]
    pub const fn int16(code: i16, value: i16) -> Self {
        Self {
            code,
            value: CodeValue::Int16(value),
        }
    }

    #[inline]
    pub const fn int32(code: i16, value: i32) -> Self {
        Self {
            code,
            value: CodeValue::Int32(value),
        }
    }

    #[inline]
    pub const fn int64(code: i16, value: i64) -> Self {
        Self {
            code,
            value: CodeValue::Int64(value),
        }
    }

    #[inline]
    pub const fn boolean(code: i16, value: bool) -> Self {
        Self {
            code,
            value: CodeValue::Bool(value),
        }
    }

    #[inline]
    pub fn bytes(code: i16, value: Vec<u8>) -> Self {
        Self {
            code,
            value: CodeValue::Bytes(value),
        }
    }
}
