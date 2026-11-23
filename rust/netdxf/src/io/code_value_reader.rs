use crate::error::Result;

/// Decoded DXF tag value returned by `ICodeValueReader`.
#[derive(Clone, Debug, PartialEq)]
pub enum CodeValue {
    None,
    String(String),
    Double(f64),
    Int16(i16),
    Int32(i32),
    Int64(i64),
    Bool(bool),
    Byte(u8),
    Bytes(Vec<u8>),
}

impl CodeValue {
    #[inline]
    pub fn as_str(&self) -> &str {
        match self {
            Self::String(s) => s.as_str(),
            _ => "",
        }
    }

    #[inline]
    pub fn to_string_value(&self) -> String {
        match self {
            Self::None => String::new(),
            Self::String(s) => s.clone(),
            Self::Double(v) => format!("{v}"),
            Self::Int16(v) => v.to_string(),
            Self::Int32(v) => v.to_string(),
            Self::Int64(v) => v.to_string(),
            Self::Bool(v) => if *v { "1" } else { "0" }.to_string(),
            Self::Byte(v) => v.to_string(),
            Self::Bytes(b) => encode_hex(b),
        }
    }

    #[inline]
    pub fn as_f64(&self) -> f64 {
        match *self {
            Self::Double(v) => v,
            Self::Int16(v) => v as f64,
            Self::Int32(v) => v as f64,
            Self::Int64(v) => v as f64,
            Self::Byte(v) => v as f64,
            Self::String(ref s) => s.trim().parse::<f64>().unwrap_or(0.0),
            _ => 0.0,
        }
    }

    #[inline]
    pub fn as_i16(&self) -> i16 {
        match *self {
            Self::Int16(v) => v,
            Self::Int32(v) => v as i16,
            Self::Int64(v) => v as i16,
            Self::Byte(v) => v as i16,
            Self::Bool(v) => i16::from(v),
            Self::Double(v) => v as i16,
            Self::String(ref s) => s.trim().parse::<i16>().unwrap_or(0),
            _ => 0,
        }
    }

    #[inline]
    pub fn as_i32(&self) -> i32 {
        match *self {
            Self::Int32(v) => v,
            Self::Int16(v) => v as i32,
            Self::Int64(v) => v as i32,
            Self::Byte(v) => v as i32,
            Self::Bool(v) => i32::from(v),
            Self::Double(v) => v as i32,
            Self::String(ref s) => s.trim().parse::<i32>().unwrap_or(0),
            _ => 0,
        }
    }

    #[inline]
    pub fn as_bool(&self) -> bool {
        match *self {
            Self::Bool(v) => v,
            Self::Int16(v) => v != 0,
            Self::Int32(v) => v != 0,
            Self::Byte(v) => v != 0,
            Self::String(ref s) => matches!(s.trim(), "1" | "true" | "TRUE"),
            _ => false,
        }
    }

    #[inline]
    pub fn as_bytes(&self) -> &[u8] {
        match self {
            Self::Bytes(b) => b.as_slice(),
            _ => &[],
        }
    }
}

/// Encodes bytes as uppercase hex string (used for DXF binary chunks and thumbnails).
pub fn encode_hex(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789ABCDEF";
    let mut out = String::with_capacity(bytes.len() * 2);
    for &b in bytes {
        out.push(HEX[(b >> 4) as usize] as char);
        out.push(HEX[(b & 0x0F) as usize] as char);
    }
    out
}

/// Decodes a hex string into raw bytes.
pub fn decode_hex(s: &str) -> Option<Vec<u8>> {
    let trimmed = s.trim();
    if trimmed.len() % 2 != 0 {
        return None;
    }
    let bytes = trimmed.as_bytes();
    let mut out = Vec::with_capacity(bytes.len() / 2);
    for pair in bytes.chunks_exact(2) {
        let hi = hex_nibble(pair[0])?;
        let lo = hex_nibble(pair[1])?;
        out.push((hi << 4) | lo);
    }
    Some(out)
}

#[inline]
fn hex_nibble(b: u8) -> Option<u8> {
    match b {
        b'0'..=b'9' => Some(b - b'0'),
        b'a'..=b'f' => Some(b - b'a' + 10),
        b'A'..=b'F' => Some(b - b'A' + 10),
        _ => None,
    }
}

/// Trait implemented by ASCII and Binary DXF tag readers (`netDxf.IO.ICodeValueReader`).
pub trait ICodeValueReader {
    fn code(&self) -> i16;
    fn value(&self) -> &CodeValue;
    fn current_position(&self) -> usize;
    fn is_eof(&self) -> bool;
    fn set_code5_is_string(&mut self, value: bool);
    fn set_skip_comments(&mut self, value: bool);
    /// Advances to the next `(code, value)` pair. Returns `Ok(false)` at clean EOF.
    fn next(&mut self) -> Result<bool>;
}
