use std::io::BufRead;

use encoding_rs::Encoding;

use crate::error::{DxfError, Result};
use crate::io::code_value_reader::{CodeValue, ICodeValueReader};
use crate::io::dxf_group_code::{DxfGroupCode, DxfTagValueType};

/// Standard 22-byte AutoCAD Binary DXF file sentinel.
pub const BINARY_DXF_SENTINEL: &[u8; 22] = b"AutoCAD Binary DXF\r\n\x1a\0";

/// High-performance Binary DXF reader (`netDxf.IO.BinaryCodeValueReader`).
pub struct BinaryCodeValueReader<R: BufRead> {
    reader: R,
    encoding: &'static Encoding,
    legacy_group_codes: bool,
    code: i16,
    value: CodeValue,
    bytes_read: usize,
    code5_is_string: bool,
    skip_comments: bool,
    eof: bool,
    str_buf: Vec<u8>,
}

impl<R: BufRead> BinaryCodeValueReader<R> {
    pub fn new(mut reader: R, encoding: &'static Encoding, legacy_group_codes: bool) -> Result<Self> {
        let mut sentinel = [0u8; 22];
        reader.read_exact(&mut sentinel).map_err(|_| {
            DxfError::InvalidBinary("incomplete 22-byte binary DXF sentinel".to_string())
        })?;
        if &sentinel != BINARY_DXF_SENTINEL {
            return Err(DxfError::InvalidBinary(
                "invalid binary DXF sentinel".to_string(),
            ));
        }
        Ok(Self {
            reader,
            encoding,
            legacy_group_codes,
            code: 0,
            value: CodeValue::None,
            bytes_read: 22,
            code5_is_string: false,
            skip_comments: false,
            eof: false,
            str_buf: Vec::with_capacity(256),
        })
    }

    fn read_null_terminated_string(&mut self) -> Result<String> {
        self.str_buf.clear();
        let n = self.reader.read_until(0, &mut self.str_buf)?;
        if n == 0 {
            return Err(DxfError::UnexpectedEof);
        }
        self.bytes_read += n;
        if self.str_buf.last() == Some(&0) {
            self.str_buf.pop();
        }
        let (cow, _, _) = self.encoding.decode(&self.str_buf);
        Ok(cow.into_owned())
    }

    fn read_u8(&mut self) -> Result<u8> {
        let mut b = [0u8; 1];
        self.reader.read_exact(&mut b)?;
        self.bytes_read += 1;
        Ok(b[0])
    }

    fn read_i16(&mut self) -> Result<i16> {
        let mut b = [0u8; 2];
        self.reader.read_exact(&mut b)?;
        self.bytes_read += 2;
        Ok(i16::from_le_bytes(b))
    }

    fn read_i32(&mut self) -> Result<i32> {
        let mut b = [0u8; 4];
        self.reader.read_exact(&mut b)?;
        self.bytes_read += 4;
        Ok(i32::from_le_bytes(b))
    }

    fn read_i64(&mut self) -> Result<i64> {
        let mut b = [0u8; 8];
        self.reader.read_exact(&mut b)?;
        self.bytes_read += 8;
        Ok(i64::from_le_bytes(b))
    }

    fn read_f64(&mut self) -> Result<f64> {
        let mut b = [0u8; 8];
        self.reader.read_exact(&mut b)?;
        self.bytes_read += 8;
        let v = f64::from_le_bytes(b);
        if !v.is_finite() {
            return Err(DxfError::InvalidBinary(format!(
                "non-finite f64 at byte offset {}",
                self.bytes_read
            )));
        }
        Ok(v)
    }

    fn read_binary_chunk(&mut self) -> Result<Vec<u8>> {
        let len = self.read_u8()? as usize;
        let mut buf = vec![0u8; len];
        self.reader.read_exact(&mut buf)?;
        self.bytes_read += len;
        Ok(buf)
    }
}

impl<R: BufRead> ICodeValueReader for BinaryCodeValueReader<R> {
    #[inline]
    fn code(&self) -> i16 {
        self.code
    }

    #[inline]
    fn value(&self) -> &CodeValue {
        &self.value
    }

    #[inline]
    fn current_position(&self) -> usize {
        self.bytes_read
    }

    #[inline]
    fn is_eof(&self) -> bool {
        self.eof
    }

    fn set_code5_is_string(&mut self, value: bool) {
        self.code5_is_string = value;
    }

    fn set_skip_comments(&mut self, value: bool) {
        self.skip_comments = value;
    }

    fn next(&mut self) -> Result<bool> {
        loop {
            // Check for clean EOF
            if self.reader.fill_buf()?.is_empty() {
                self.eof = true;
                return Ok(false);
            }
            let code = if self.legacy_group_codes {
                let b = self.read_u8()?;
                if b == 255 {
                    self.read_i16()?
                } else {
                    b as i16
                }
            } else {
                self.read_i16()?
            };
            self.code = code;

            if code == 5 && self.code5_is_string {
                let s = self.read_null_terminated_string()?;
                self.value = CodeValue::String(s);
            } else {
                self.value = match DxfGroupCode::try_get_value_type(code) {
                    Some(DxfTagValueType::String) | Some(DxfTagValueType::Handle) | None => {
                        CodeValue::String(self.read_null_terminated_string()?)
                    }
                    Some(DxfTagValueType::Double) => CodeValue::Double(self.read_f64()?),
                    Some(DxfTagValueType::Int16) => {
                        // In modern binary DXF, codes 280..=289 are single bytes in AutoCAD
                        if (280..=289).contains(&code) {
                            CodeValue::Int16(self.read_u8()? as i16)
                        } else {
                            CodeValue::Int16(self.read_i16()?)
                        }
                    }
                    Some(DxfTagValueType::Int32) => CodeValue::Int32(self.read_i32()?),
                    Some(DxfTagValueType::Int64) => CodeValue::Int64(self.read_i64()?),
                    Some(DxfTagValueType::Boolean) => CodeValue::Bool(self.read_u8()? != 0),
                    Some(DxfTagValueType::BinaryData) => {
                        CodeValue::Bytes(self.read_binary_chunk()?)
                    }
                };
            }

            if self.skip_comments && self.code == 999 {
                continue;
            }
            return Ok(true);
        }
    }
}
