use std::io::BufRead;

use encoding_rs::Encoding;

use crate::error::{DxfError, Result};
use crate::io::code_value_reader::{decode_hex, CodeValue, ICodeValueReader};
use crate::io::dxf_group_code::{DxfGroupCode, DxfTagValueType};

/// High-performance ASCII DXF `(group_code, value)` reader (`netDxf.IO.TextCodeValueReader`).
///
/// Reuses internal byte/string buffers across lines to minimize heap allocations.
pub struct TextCodeValueReader<R: BufRead> {
    reader: R,
    encoding: &'static Encoding,
    code: i16,
    value: CodeValue,
    line_number: usize,
    code5_is_string: bool,
    skip_comments: bool,
    eof: bool,
    raw_buf: Vec<u8>,
}

impl<R: BufRead> TextCodeValueReader<R> {
    pub fn new(reader: R, encoding: &'static Encoding) -> Self {
        Self {
            reader,
            encoding,
            code: 0,
            value: CodeValue::None,
            line_number: 0,
            code5_is_string: false,
            skip_comments: false,
            eof: false,
            raw_buf: Vec::with_capacity(256),
        }
    }

    pub fn set_encoding(&mut self, encoding: &'static Encoding) {
        self.encoding = encoding;
    }

    fn read_line_decoded(&mut self) -> Result<Option<String>> {
        self.raw_buf.clear();
        let n = self.reader.read_until(b'\n', &mut self.raw_buf)?;
        if n == 0 {
            return Ok(None);
        }
        // Strip trailing \n and \r
        while self.raw_buf.last().is_some_and(|&b| b == b'\n' || b == b'\r') {
            self.raw_buf.pop();
        }
        // Strip UTF-8 BOM on very first line
        if self.line_number == 0 && self.raw_buf.starts_with(&[0xEF, 0xBB, 0xBF]) {
            self.raw_buf.drain(0..3);
        }
        let (cow, _, _) = self.encoding.decode(&self.raw_buf);
        self.line_number += 1;
        Ok(Some(cow.into_owned()))
    }

    fn trim_ascii_ws(s: &str) -> &str {
        s.trim_matches(|c| c == ' ' || c == '\t')
    }

    fn parse_handle(&self, code: i16, s: &str) -> Result<String> {
        let trimmed = Self::trim_ascii_ws(s);
        if trimmed.is_empty()
            || trimmed.len() > 16
            || !trimmed.bytes().all(|b| b.is_ascii_hexdigit())
        {
            return Err(DxfError::Parse {
                line: self.line_number,
                message: format!("invalid handle '{s}' for group code {code}"),
            });
        }
        let stripped = trimmed.trim_start_matches('0');
        if stripped.is_empty() {
            Ok("0".to_string())
        } else {
            Ok(stripped.to_ascii_uppercase())
        }
    }

    fn parse_value(&self, code: i16, s: String) -> Result<CodeValue> {
        if code == 5 && self.code5_is_string {
            return Ok(CodeValue::String(s));
        }
        match DxfGroupCode::try_get_value_type(code) {
            Some(DxfTagValueType::String) | None => Ok(CodeValue::String(s)),
            Some(DxfTagValueType::Handle) => Ok(CodeValue::String(self.parse_handle(code, &s)?)),
            Some(DxfTagValueType::Double) => {
                let trimmed = Self::trim_ascii_ws(&s);
                let v = trimmed.parse::<f64>().map_err(|_| DxfError::Parse {
                    line: self.line_number,
                    message: format!("invalid f64 value '{s}' for group code {code}"),
                })?;
                if !v.is_finite() {
                    return Err(DxfError::Parse {
                        line: self.line_number,
                        message: format!("non-finite f64 value '{s}' for group code {code}"),
                    });
                }
                Ok(CodeValue::Double(v))
            }
            Some(DxfTagValueType::Int16) => {
                let trimmed = Self::trim_ascii_ws(&s);
                let v = trimmed.parse::<i16>().map_err(|_| DxfError::Parse {
                    line: self.line_number,
                    message: format!("invalid i16 value '{s}' for group code {code}"),
                })?;
                Ok(CodeValue::Int16(v))
            }
            Some(DxfTagValueType::Int32) => {
                let trimmed = Self::trim_ascii_ws(&s);
                let v = trimmed.parse::<i32>().map_err(|_| DxfError::Parse {
                    line: self.line_number,
                    message: format!("invalid i32 value '{s}' for group code {code}"),
                })?;
                Ok(CodeValue::Int32(v))
            }
            Some(DxfTagValueType::Int64) => {
                let trimmed = Self::trim_ascii_ws(&s);
                let v = trimmed.parse::<i64>().map_err(|_| DxfError::Parse {
                    line: self.line_number,
                    message: format!("invalid i64 value '{s}' for group code {code}"),
                })?;
                Ok(CodeValue::Int64(v))
            }
            Some(DxfTagValueType::Boolean) => {
                let trimmed = Self::trim_ascii_ws(&s);
                let n = trimmed.parse::<i16>().map_err(|_| DxfError::Parse {
                    line: self.line_number,
                    message: format!("invalid boolean value '{s}' for group code {code}"),
                })?;
                match n {
                    0 => Ok(CodeValue::Bool(false)),
                    1 => Ok(CodeValue::Bool(true)),
                    _ => Err(DxfError::Parse {
                        line: self.line_number,
                        message: format!("out-of-range boolean value '{s}' for group code {code}"),
                    }),
                }
            }
            Some(DxfTagValueType::BinaryData) => {
                let bytes = decode_hex(&s).ok_or_else(|| DxfError::Parse {
                    line: self.line_number,
                    message: format!("invalid hex binary chunk for group code {code}"),
                })?;
                Ok(CodeValue::Bytes(bytes))
            }
        }
    }
}

impl<R: BufRead> ICodeValueReader for TextCodeValueReader<R> {
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
        self.line_number
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
            let Some(code_line) = self.read_line_decoded()? else {
                self.eof = true;
                return Ok(false);
            };
            if code_line.contains('\0') {
                return Err(DxfError::Parse {
                    line: self.line_number,
                    message: "NUL byte in DXF group code".to_string(),
                });
            }
            let code = code_line.trim().parse::<i16>().map_err(|_| DxfError::Parse {
                line: self.line_number,
                message: format!("invalid DXF group code '{code_line}'"),
            })?;

            let value_line = self.read_line_decoded()?.ok_or(DxfError::UnexpectedEof)?;
            self.code = code;
            // Preserve exact whitespace for text strings (codes 1, 3, 300..309, 999, 1000)
            let preserve_ws = matches!(code, 1 | 3 | 300..=309 | 999 | 1000);
            self.value = if preserve_ws {
                CodeValue::String(value_line)
            } else {
                self.parse_value(code, value_line)?
            };

            if self.skip_comments && self.code == 999 {
                continue;
            }
            return Ok(true);
        }
    }
}
