use std::io::Write;

use crate::error::Result;
use crate::io::code_value_reader::encode_hex;
use crate::io::code_value_writer::ICodeValueWriter;

/// High-performance ASCII DXF writer (`netDxf.IO.TextCodeValueWriter`).
pub struct TextCodeValueWriter<W: Write> {
    writer: W,
}

impl<W: Write> TextCodeValueWriter<W> {
    pub fn new(writer: W) -> Self {
        Self { writer }
    }

    pub fn into_inner(self) -> W {
        self.writer
    }
}

impl<W: Write> ICodeValueWriter for TextCodeValueWriter<W> {
    #[inline]
    fn write_str(&mut self, code: i16, value: &str) -> Result<()> {
        writeln!(self.writer, "{code:3}\r\n{value}\r")?;
        Ok(())
    }

    #[inline]
    fn write_f64(&mut self, code: i16, value: f64) -> Result<()> {
        let v = if value == 0.0 { 0.0 } else { value };
        writeln!(self.writer, "{code:3}\r\n{v}\r")?;
        Ok(())
    }

    #[inline]
    fn write_i16(&mut self, code: i16, value: i16) -> Result<()> {
        writeln!(self.writer, "{code:3}\r\n{value}\r")?;
        Ok(())
    }

    #[inline]
    fn write_i32(&mut self, code: i16, value: i32) -> Result<()> {
        writeln!(self.writer, "{code:3}\r\n{value}\r")?;
        Ok(())
    }

    #[inline]
    fn write_i64(&mut self, code: i16, value: i64) -> Result<()> {
        writeln!(self.writer, "{code:3}\r\n{value}\r")?;
        Ok(())
    }

    #[inline]
    fn write_bool(&mut self, code: i16, value: bool) -> Result<()> {
        let n = u8::from(value);
        writeln!(self.writer, "{code:3}\r\n{n}\r")?;
        Ok(())
    }

    #[inline]
    fn write_byte(&mut self, code: i16, value: u8) -> Result<()> {
        writeln!(self.writer, "{code:3}\r\n{value}\r")?;
        Ok(())
    }

    #[inline]
    fn write_bytes(&mut self, code: i16, value: &[u8]) -> Result<()> {
        let hex = encode_hex(value);
        writeln!(self.writer, "{code:3}\r\n{hex}\r")?;
        Ok(())
    }

    fn flush(&mut self) -> Result<()> {
        self.writer.flush()?;
        Ok(())
    }
}
