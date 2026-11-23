use crate::error::Result;

/// Trait implemented by ASCII and Binary DXF tag writers (`netDxf.IO.ICodeValueWriter`).
pub trait ICodeValueWriter {
    fn write_str(&mut self, code: i16, value: &str) -> Result<()>;
    fn write_f64(&mut self, code: i16, value: f64) -> Result<()>;
    fn write_i16(&mut self, code: i16, value: i16) -> Result<()>;
    fn write_i32(&mut self, code: i16, value: i32) -> Result<()>;
    fn write_i64(&mut self, code: i16, value: i64) -> Result<()>;
    fn write_bool(&mut self, code: i16, value: bool) -> Result<()>;
    fn write_byte(&mut self, code: i16, value: u8) -> Result<()>;
    fn write_bytes(&mut self, code: i16, value: &[u8]) -> Result<()>;
    fn flush(&mut self) -> Result<()>;
}
