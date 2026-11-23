#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadContour, CadGeometry, ICacheableEntityProjector, IEntityProjector};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

pub struct ShxFontReader {
    data: Vec<u8>,
    pos: usize,
    name: String,
}

impl ShxFontReader {
    pub fn new(bytes: &[u8], name: impl Into<String>) -> Self {
        Self {
            data: bytes.to_vec(),
            pos: 0,
            name: name.into(),
        }
    }

    pub fn name(&self) -> &str {
        &self.name
    }

    pub fn remaining(&self) -> usize {
        self.data.len().saturating_sub(self.pos)
    }

    pub fn byte(&mut self) -> Result<u8, String> {
        if self.pos >= self.data.len() {
            return Err("Unexpected end of SHX stream.".into());
        }
        let b = self.data[self.pos];
        self.pos += 1;
        Ok(b)
    }

    pub fn signed(&mut self) -> Result<i8, String> {
        Ok(self.byte()? as i8)
    }

    pub fn word(&mut self) -> Result<u16, String> {
        let lo = self.byte()? as u16;
        let hi = self.byte()? as u16;
        Ok(lo | (hi << 8))
    }

    pub fn take(&mut self, count: usize) -> Result<Vec<u8>, String> {
        if self.remaining() < count {
            return Err("Unexpected end of SHX stream.".into());
        }
        let slice = self.data[self.pos..self.pos + count].to_vec();
        self.pos += count;
        Ok(slice)
    }

    pub fn read_ascii(
        text: &str,
        name: impl Into<String>,
        options: &ShxReadOptions,
    ) -> Result<ShxFont, String> {
        if text.len() > options.maximum_bytes {
            return Err("SHP ASCII input exceeds configured byte limit.".into());
        }
        let mut symbols = BTreeMap::new();
        let mut above = 6u8;
        let mut below = 2u8;
        let mut current_code: Option<u32> = None;
        let mut current_name = String::new();
        let mut current_bytes: Vec<u8> = Vec::new();
        for raw_line in text.lines() {
            let line = raw_line.split(';').next().unwrap_or("").trim();
            if line.is_empty() {
                continue;
            }
            if let Some(rest) = line.strip_prefix('*') {
                if let Some(code) = current_code.take() {
                    if code == 0 && current_bytes.len() >= 2 {
                        above = current_bytes[0];
                        below = current_bytes[1];
                    } else {
                        symbols.insert(
                            code,
                            ShxSymbol {
                                code,
                                name: std::mem::take(&mut current_name),
                                data: std::mem::take(&mut current_bytes),
                            },
                        );
                    }
                }
                let parts: Vec<&str> = rest.split(',').map(str::trim).collect();
                if let Some(first) = parts.first() {
                    let parsed = if let Some(hex) = first.strip_prefix("0x").or_else(|| first.strip_prefix("0X")) {
                        u32::from_str_radix(hex, 16).ok()
                    } else {
                        first.parse::<u32>().ok()
                    };
                    current_code = parsed;
                    current_name = parts.get(2).copied().unwrap_or_default().to_string();
                    current_bytes.clear();
                }
            } else if current_code.is_some() {
                for tok in line.split(',') {
                    let tok = tok.trim();
                    if tok.is_empty() {
                        continue;
                    }
                    if let Some(hex) = tok.strip_prefix("0x").or_else(|| tok.strip_prefix("0X")) {
                        if let Ok(v) = u8::from_str_radix(hex, 16) {
                            current_bytes.push(v);
                        }
                    } else if let Ok(v) = tok.parse::<i16>() {
                        current_bytes.push(v as u8);
                    }
                }
            }
        }
        if let Some(code) = current_code {
            if code == 0 && current_bytes.len() >= 2 {
                above = current_bytes[0];
                below = current_bytes[1];
            } else {
                symbols.insert(
                    code,
                    ShxSymbol {
                        code,
                        name: current_name,
                        data: current_bytes,
                    },
                );
            }
        }
        Ok(ShxFont {
            name: name.into(),
            encoding: ShxEncoding::Ascii,
            embedding: ShxEmbedding::Horizontal,
            above,
            below,
            symbols,
        })
    }

    pub fn read_binary(
        bytes: &[u8],
        name: impl Into<String>,
        options: &ShxReadOptions,
    ) -> Result<ShxFont, String> {
        Self::read(bytes, name, options)
    }

    pub fn read(
        bytes: &[u8],
        name: impl Into<String>,
        options: &ShxReadOptions,
    ) -> Result<ShxFont, String> {
        if bytes.len() > options.maximum_bytes {
            return Err("SHX input exceeds configured byte limit.".into());
        }
        let header_end = bytes
            .iter()
            .position(|&b| b == 0x1A)
            .unwrap_or(bytes.len().min(64));
        let header = String::from_utf8_lossy(&bytes[..header_end]);
        let encoding = if header.contains("unifont") {
            ShxEncoding::Unicode
        } else if header.contains("bigfont") {
            ShxEncoding::BigFont
        } else {
            ShxEncoding::Ascii
        };
        let mut above = 6u8;
        let mut below = 2u8;
        let mut symbols = BTreeMap::new();
        if header_end < bytes.len() && bytes[header_end] == 0x1A {
            let mut reader = Self::new(&bytes[header_end + 1..], "");
            if encoding == ShxEncoding::Unicode && reader.remaining() >= 6 {
                let count = reader.word()? as usize;
                let info_len = reader.word()? as usize;
                let info = reader.take(info_len.min(reader.remaining()))?;
                if let Some(nul) = info.iter().position(|&b| b == 0) {
                    if nul + 2 < info.len() {
                        above = info[nul + 1];
                        below = info[nul + 2];
                    }
                }
                for _ in 0..count.min(options.maximum_symbols) {
                    if reader.remaining() < 4 {
                        break;
                    }
                    let code = reader.word()? as u32;
                    let len = reader.word()? as usize;
                    let data = reader.take(len.min(reader.remaining()))?;
                    symbols.insert(
                        code,
                        ShxSymbol {
                            code,
                            name: String::new(),
                            data,
                        },
                    );
                }
            }
        }
        Ok(ShxFont {
            name: name.into(),
            encoding,
            embedding: ShxEmbedding::Horizontal,
            above,
            below,
            symbols,
        })
    }
}

