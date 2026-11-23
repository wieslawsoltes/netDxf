use std::io::Write;

use crate::error::{DxfError, Result};
use crate::io::binary_code_value_reader::BINARY_DXF_SENTINEL;
use crate::io::code_value_writer::ICodeValueWriter;

/// High-performance Binary DXF writer (`netDxf.IO.BinaryCodeValueWriter`).
pub struct BinaryCodeValueWriter<W: Write> {
    writer: W,
    legacy_group_codes: bool,
}

impl<W: Write> BinaryCodeValueWriter<W> {
    pub fn new(mut writer: W, legacy_group_codes: bool) -> Result<Self> {
        writer.write_all(BINARY_DXF_SENTINEL)?;
        Ok(Self {
            writer,
            legacy_group_codes,
        })
    }

    pub fn into_inner(self) -> W {
        self.writer
    }

    #[inline]
    fn write_code(&mut self, code: i16) -> Result<()> {
        if self.legacy_group_codes {
            if code >= 255 {
                self.writer.write_all(&[255])?;
                self.writer.write_all(&code.to_le_bytes())?;
            } else {
                self.writer.write_all(&[code as u8])?;
            }
        } else {
            self.writer.write_all(&code.to_le_bytes())?;
        }
        Ok(())
    }
}

impl<W: Write> ICodeValueWriter for BinaryCodeValueWriter<W> {
    fn write_str(&mut self, code: i16, value: &str) -> Result<()> {
        self.write_code(code)?;
        self.writer.write_all(value.as_bytes())?;
        self.writer.write_all(&[0])?;
        Ok(())
    }

    fn write_f64(&mut self, code: i16, value: f64) -> Result<()> {
        self.write_code(code)?;
        let v = if value == 0.0 { 0.0 } else { value };
        self.writer.write_all(&v.to_le_bytes())?;
        Ok(())
    }

    fn write_i16(&mut self, code: i16, value: i16) -> Result<()> {
        self.write_code(code)?;
        if (280..=289).contains(&code) {
            self.writer.write_all(&[value as u8])?;
        } else {
            self.writer.write_all(&value.to_le_bytes())?;
        }
        Ok(())
    }

    fn write_i32(&mut self, code: i16, value: i32) -> Result<()> {
        self.write_code(code)?;
        self.writer.write_all(&value.to_le_bytes())?;
        Ok(())
    }

    fn write_i64(&mut self, code: i16, value: i64) -> Result<()> {
        self.write_code(code)?;
        self.writer.write_all(&value.to_le_bytes())?;
        Ok(())
    }

    fn write_bool(&mut self, code: i16, value: bool) -> Result<()> {
        self.write_code(code)?;
        self.writer.write_all(&[u8::from(value)])?;
        Ok(())
    }

    fn write_byte(&mut self, code: i16, value: u8) -> Result<()> {
        self.write_code(code)?;
        self.writer.write_all(&[value])?;
        Ok(())
    }

    fn write_bytes(&mut self, code: i16, value: &[u8]) -> Result<()> {
        if value.len() > 127 {
            return Err(DxfError::InvalidBinary(format!(
                "binary chunk length {} exceeds 127 bytes",
                value.len()
            )));
        }
        self.write_code(code)?;
        self.writer.write_all(&[value.len() as u8])?;
        self.writer.write_all(value)?;
        Ok(())
    }

    fn flush(&mut self) -> Result<()> {
        self.writer.flush()?;
        Ok(())
    }
}
