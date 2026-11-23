use netdxf::header::DxfVersion;
use netdxf::DxfDocument;
use serde::{Deserialize, Serialize};
use thiserror::Error;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DrawingEncoding {
    #[default]
    DxfAscii,
    DxfBinary,
    Dwg,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingExchangeLimits {
    pub maximum_input_bytes: usize,
    pub maximum_output_bytes: usize,
    pub maximum_diagnostics: usize,
}

impl Default for DrawingExchangeLimits {
    fn default() -> Self {
        Self {
            maximum_input_bytes: 20 * 1024 * 1024,
            maximum_output_bytes: 64 * 1024 * 1024,
            maximum_diagnostics: 2_048,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingExchangeDiagnostic {
    pub stage: String,
    pub kind: String,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DrawingExchangeResult {
    pub content: Vec<u8>,
    pub encoding: DrawingEncoding,
    pub diagnostics: Vec<DrawingExchangeDiagnostic>,
}

#[derive(Debug, Clone, Error, PartialEq, Eq)]
#[error("{message}")]
pub struct DrawingExchangeException {
    pub message: String,
    pub diagnostics: Vec<DrawingExchangeDiagnostic>,
}

/// Bounded DXF/DWG exchange converter built on `netdxf`.
#[derive(Debug, Clone, Default)]
pub struct DrawingExchange {
    pub limits: DrawingExchangeLimits,
}

impl DrawingExchange {
    pub fn new(limits: DrawingExchangeLimits) -> Self {
        Self { limits }
    }

    #[inline]
    pub fn provider(&self) -> &'static str {
        "netdxf"
    }

    pub fn read(&self, data: &[u8]) -> Result<DxfDocument, DrawingExchangeException> {
        self.load_dxf(data)
    }

    pub fn write(
        &self,
        doc: &mut DxfDocument,
        encoding: DrawingEncoding,
        version: Option<DxfVersion>,
    ) -> Result<DrawingExchangeResult, DrawingExchangeException> {
        self.export_dxf(doc, encoding, version)
    }

    pub fn read_byte(data: &[u8], offset: usize) -> Option<u8> {
        data.get(offset).copied()
    }

    pub fn write_byte(buf: &mut Vec<u8>, byte: u8) {
        buf.push(byte);
    }

    pub fn seek(offset: usize, len: usize) -> usize {
        offset.min(len)
    }

    pub fn set_length(buf: &mut Vec<u8>, new_len: usize) {
        buf.resize(new_len, 0);
    }

    pub fn detect_encoding(data: &[u8]) -> DrawingEncoding {
        if data.starts_with(b"AC10") && !data.starts_with(b"AutoCAD Binary DXF") {
            DrawingEncoding::Dwg
        } else if data.starts_with(b"AutoCAD Binary DXF") {
            DrawingEncoding::DxfBinary
        } else {
            DrawingEncoding::DxfAscii
        }
    }

    #[inline]
    pub fn is_dwg(data: &[u8]) -> bool {
        Self::detect_encoding(data) == DrawingEncoding::Dwg
    }

    pub fn write_versions() -> &'static [DxfVersion] {
        &[
            DxfVersion::AutoCad12,
            DxfVersion::AutoCad2000,
            DxfVersion::AutoCad2004,
            DxfVersion::AutoCad2007,
            DxfVersion::AutoCad2010,
            DxfVersion::AutoCad2013,
            DxfVersion::AutoCad2018,
        ]
    }

    pub fn convert(
        &self,
        data: &[u8],
        target_encoding: DrawingEncoding,
        target_version: Option<DxfVersion>,
    ) -> Result<DrawingExchangeResult, DrawingExchangeException> {
        let mut doc = self.load_dxf(data)?;
        self.export_dxf(&mut doc, target_encoding, target_version)
    }

    pub fn load_dxf(&self, data: &[u8]) -> Result<DxfDocument, DrawingExchangeException> {
        if data.len() > self.limits.maximum_input_bytes {
            return Err(DrawingExchangeException {
                message: "Drawing input exceeds configured byte limit.".into(),
                diagnostics: vec![DrawingExchangeDiagnostic {
                    stage: "Load".into(),
                    kind: "LimitExceeded".into(),
                    message: format!("{} bytes exceeds limit", data.len()),
                }],
            });
        }
        DxfDocument::load_from_reader(std::io::Cursor::new(data)).map_err(|e| DrawingExchangeException {
            message: e.to_string(),
            diagnostics: vec![DrawingExchangeDiagnostic {
                stage: "Load".into(),
                kind: "DxfReadError".into(),
                message: e.to_string(),
            }],
        })
    }

    pub fn export_dxf(
        &self,
        doc: &mut DxfDocument,
        encoding: DrawingEncoding,
        version: Option<DxfVersion>,
    ) -> Result<DrawingExchangeResult, DrawingExchangeException> {
        if let Some(ver) = version {
            doc.drawing_variables.acad_ver = ver;
        }
        let is_binary = matches!(encoding, DrawingEncoding::DxfBinary);
        let mut buf = Vec::new();
        doc.save_to_writer(&mut buf, is_binary)
            .map_err(|e| DrawingExchangeException {
                message: e.to_string(),
                diagnostics: vec![DrawingExchangeDiagnostic {
                    stage: "Export".into(),
                    kind: "DxfWriteError".into(),
                    message: e.to_string(),
                }],
            })?;
        if buf.len() > self.limits.maximum_output_bytes {
            return Err(DrawingExchangeException {
                message: "Drawing output exceeds configured byte limit.".into(),
                diagnostics: Vec::new(),
            });
        }
        Ok(DrawingExchangeResult {
            content: buf,
            encoding,
            diagnostics: Vec::new(),
        })
    }
}
