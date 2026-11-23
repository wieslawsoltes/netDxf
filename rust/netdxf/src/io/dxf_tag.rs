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

    #[inline]
    pub fn create_dimension_style_arrow_name(code: i16, name: impl Into<String>) -> Self {
        Self::string(code, name)
    }

    #[inline]
    pub fn create_opaque_object_code5(handle: impl Into<String>) -> Self {
        Self::string(5, handle)
    }

    #[inline]
    pub fn handle_kind(&self) -> i16 {
        self.code
    }

    #[inline]
    pub fn value_type(&self) -> &'static str {
        match self.value {
            CodeValue::String(_) => "String",
            CodeValue::Double(_) => "Double",
            CodeValue::Int16(_) => "Int16",
            CodeValue::Int32(_) => "Int32",
            CodeValue::Int64(_) => "Int64",
            CodeValue::Bool(_) => "Bool",
            CodeValue::Byte(_) => "Byte",
            CodeValue::Bytes(_) => "Bytes",
            CodeValue::None => "None",
        }
    }
}
