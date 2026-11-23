use crate::entities::acis_sat_chunk::AcisSatChunk;
use crate::error::{DxfError, Result};

/// Shared ACIS SAT payload container for `3DSOLID`, `BODY`, and `REGION` (`netDxf.Entities.AcisEntity`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AcisEntityData {
    pub modeler_format_version: i16,
    encoded_sat_chunks: Vec<AcisSatChunk>,
    sat_lines: Vec<String>,
}

impl AcisEntityData {
    pub const MAXIMUM_SAT_CHUNKS: usize = 65_536;
    pub const MAXIMUM_SAT_CHARACTERS: usize = 16 * 1024 * 1024;
    pub const MAXIMUM_SAT_LINE_CHARACTERS: usize = 1024 * 1024;

    pub fn new() -> Self {
        Self {
            modeler_format_version: 1,
            encoded_sat_chunks: Vec::new(),
            sat_lines: Vec::new(),
        }
    }

    #[inline]
    pub fn encoded_sat_chunks(&self) -> &[AcisSatChunk] {
        &self.encoded_sat_chunks
    }

    #[inline]
    pub fn sat_lines(&self) -> &[String] {
        &self.sat_lines
    }

    /// Encodes a single printable ASCII SAT line using DXF's ACIS SAT text substitution.
    pub fn encode_sat_line(text: &str) -> Result<String> {
        if !text.bytes().all(|b| (32..=126).contains(&b)) {
            return Err(DxfError::InvalidValue(
                "SAT text must contain printable ASCII without newline delimiters".to_string(),
            ));
        }
        let mut out = String::with_capacity(text.len());
        for b in text.bytes() {
            match b {
                b' ' => out.push(' '),
                b'_' => out.push('@'),
                b'@' => out.push('_'),
                b'A'..=b'^' => {
                    out.push((b'^' - (b - b'A')) as char);
                    if b == b'A' {
                        out.push(' ');
                    }
                }
                _ => out.push((b ^ 0x5F) as char),
            }
        }
        Ok(out)
    }

    /// Decodes a DXF-obfuscated ACIS SAT line back to printable ASCII.
    pub fn decode_sat_line(text: &str) -> Result<String> {
        let bytes = text.as_bytes();
        let mut out = String::with_capacity(bytes.len());
        let mut i = 0;
        while i < bytes.len() {
            let b = bytes[i];
            match b {
                b' ' => out.push(' '),
                b'@' => out.push('_'),
                b'_' => out.push('@'),
                b'A'..=b'^' => {
                    out.push((b'A' + (b'^' - b)) as char);
                    if b == b'^' {
                        i += 1;
                        if i >= bytes.len() || bytes[i] != b' ' {
                            return Err(DxfError::InvalidValue(
                                "Malformed SAT encoded A escape".to_string(),
                            ));
                        }
                    }
                }
                _ => out.push((b ^ 0x5F) as char),
            }
            i += 1;
        }
        Ok(out)
    }

    /// Replaces the payload from decoded printable ASCII lines, splitting into <=255-char chunks.
    pub fn set_sat_lines<S: AsRef<str>>(&mut self, lines: impl IntoIterator<Item = S>) -> Result<()> {
        let mut chunks = Vec::new();
        let mut total = 0usize;
        for line in lines {
            let s = line.as_ref();
            if s.len() > Self::MAXIMUM_SAT_LINE_CHARACTERS {
                return Err(DxfError::InvalidValue(
                    "SAT line exceeds maximum length".to_string(),
                ));
            }
            let encoded = Self::encode_sat_line(s)?;
            if encoded.len() > Self::MAXIMUM_SAT_LINE_CHARACTERS
                || encoded.len() > Self::MAXIMUM_SAT_CHARACTERS - total
            {
                return Err(DxfError::InvalidValue(
                    "SAT character limit exceeded".to_string(),
                ));
            }
            total += encoded.len();
            let mut offset = 0;
            while offset < encoded.len() || offset == 0 {
                if chunks.len() >= Self::MAXIMUM_SAT_CHUNKS {
                    return Err(DxfError::InvalidValue(
                        "SAT chunk limit exceeded".to_string(),
                    ));
                }
                let end = (offset + AcisSatChunk::MAXIMUM_LENGTH).min(encoded.len());
                let code = if offset == 0 { 1 } else { 3 };
                chunks.push(AcisSatChunk::new(code, &encoded[offset..end])?);
                offset = end;
                if offset == 0 {
                    break;
                }
            }
        }
        self.set_encoded_sat_chunks(chunks)
    }

    /// Replaces the payload from exact encoded `(1 / 3)` chunks and decodes logical SAT lines.
    pub fn set_encoded_sat_chunks(
        &mut self,
        chunks: impl IntoIterator<Item = AcisSatChunk>,
    ) -> Result<()> {
        let mut out_chunks = Vec::new();
        let mut decoded_lines = Vec::new();
        let mut current_line: Option<String> = None;
        let mut total = 0usize;

        for chunk in chunks {
            if out_chunks.len() >= Self::MAXIMUM_SAT_CHUNKS
                || chunk.text.len() > Self::MAXIMUM_SAT_CHARACTERS - total
            {
                return Err(DxfError::InvalidValue(
                    "SAT payload limit exceeded".to_string(),
                ));
            }
            total += chunk.text.len();
            if chunk.group_code == 1 {
                if let Some(prev) = current_line.take() {
                    decoded_lines.push(Self::decode_sat_line(&prev)?);
                }
                current_line = Some(String::new());
            }
            let line = current_line.as_mut().ok_or_else(|| {
                DxfError::InvalidValue(
                    "A SAT continuation (code 3) requires a preceding code 1".to_string(),
                )
            })?;
            if chunk.text.len() > Self::MAXIMUM_SAT_LINE_CHARACTERS - line.len() {
                return Err(DxfError::InvalidValue(
                    "SAT logical line limit exceeded".to_string(),
                ));
            }
            line.push_str(&chunk.text);
            out_chunks.push(chunk);
        }
        if let Some(last) = current_line {
            decoded_lines.push(Self::decode_sat_line(&last)?);
        }
        self.encoded_sat_chunks = out_chunks;
        self.sat_lines = decoded_lines;
        Ok(())
    }
}

impl Default for AcisEntityData {
    fn default() -> Self {
        Self::new()
    }
}
