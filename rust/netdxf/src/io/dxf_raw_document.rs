use std::fs::File;
use std::io::{BufReader, Cursor, Read, Seek, SeekFrom, Write};
use std::path::Path;

use encoding_rs::{Encoding, UTF_8, WINDOWS_1252};

use crate::dxf_object_code::DxfObjectCode;
use crate::error::{DxfError, Result};
use crate::io::binary_code_value_reader::{BinaryCodeValueReader, BINARY_DXF_SENTINEL};
use crate::io::binary_code_value_writer::BinaryCodeValueWriter;
use crate::io::code_value_reader::{CodeValue, ICodeValueReader};
use crate::io::code_value_writer::ICodeValueWriter;
use crate::io::dxf_atomic_file::DxfAtomicFile;
use crate::io::dxf_raw_record::DxfRawRecord;
use crate::io::dxf_raw_section::DxfRawSection;
use crate::io::dxf_reader::DxfReader;
use crate::io::dxf_tag::DxfTag;
use crate::io::text_code_value_reader::TextCodeValueReader;
use crate::io::text_code_value_writer::TextCodeValueWriter;

/// Lossless, immutable-style raw tag-level DXF document (`netDxf.IO.DxfRawDocument`).
///
/// Preserves every `(group_code, value)` tag, comment, section, and record across ASCII and Binary
/// DXF transports without semantic reinterpretation.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfRawDocument {
    pub tags: Vec<DxfTag>,
    pub sections: Vec<DxfRawSection>,
    pub is_binary: bool,
}

impl DxfRawDocument {
    /// Creates a `DxfRawDocument` from an existing sequence of tags and indexes its sections/records.
    pub fn create(tags: Vec<DxfTag>, is_binary: bool) -> Self {
        let sections = Self::index_sections(&tags);
        Self {
            tags,
            sections,
            is_binary,
        }
    }

    /// Loads a `DxfRawDocument` from a file path.
    pub fn load(path: impl AsRef<Path>) -> Result<Self> {
        let file = File::open(path)?;
        Self::load_from_reader(BufReader::new(file))
    }

    /// Loads a `DxfRawDocument` from an in-memory byte slice.
    pub fn load_from_bytes(bytes: &[u8]) -> Result<Self> {
        Self::load_from_reader(Cursor::new(bytes))
    }

    /// Loads a `DxfRawDocument` from any seekable reader (ASCII or Binary).
    pub fn load_from_reader<R: Read + Seek>(mut stream: R) -> Result<Self> {
        let start = stream.stream_position()?;
        let (version, is_binary) = DxfReader::check_version(&mut stream)?;
        let encoding: &'static Encoding = if version.uses_utf8() {
            UTF_8
        } else {
            WINDOWS_1252
        };
        stream.seek(SeekFrom::Start(start))?;

        let mut sentinel = [0u8; 22];
        let detected_binary = match stream.read_exact(&mut sentinel) {
            Ok(()) => &sentinel == BINARY_DXF_SENTINEL,
            Err(_) => false,
        };
        stream.seek(SeekFrom::Start(start))?;
        let binary = is_binary || detected_binary;

        let buf = BufReader::new(stream);
        let tags = if binary {
            let mut reader = BinaryCodeValueReader::new(buf, encoding, false)?;
            Self::read_all_tags(&mut reader)?
        } else {
            let mut reader = TextCodeValueReader::new(buf, encoding);
            Self::read_all_tags(&mut reader)?
        };

        Ok(Self::create(tags, binary))
    }

    fn read_all_tags(reader: &mut dyn ICodeValueReader) -> Result<Vec<DxfTag>> {
        let mut tags = Vec::new();
        while reader.next()? {
            let code = reader.code();
            let value = reader.value().clone();
            let is_eof = code == 0 && value.as_str().eq_ignore_ascii_case(DxfObjectCode::END_OF_FILE);
            tags.push(DxfTag::new(code, value));
            if is_eof {
                break;
            }
        }
        if tags.is_empty() {
            return Err(DxfError::UnexpectedEof);
        }
        Ok(tags)
    }

    fn index_sections(tags: &[DxfTag]) -> Vec<DxfRawSection> {
        let mut sections = Vec::new();
        let mut i = 0;
        while i < tags.len() {
            if tags[i].code == 0
                && tags[i]
                    .value
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::BEGIN_SECTION)
            {
                let start_idx = i;
                let mut name = String::new();
                let mut body_start = i + 1;
                while body_start < tags.len() && tags[body_start].code == 999 {
                    body_start += 1;
                }
                if body_start < tags.len() && tags[body_start].code == 2 {
                    name = tags[body_start].value.as_str().to_ascii_uppercase();
                    body_start += 1;
                }
                let mut end_idx = body_start;
                while end_idx < tags.len() {
                    if tags[end_idx].code == 0
                        && tags[end_idx]
                            .value
                            .as_str()
                            .eq_ignore_ascii_case(DxfObjectCode::END_SECTION)
                    {
                        break;
                    }
                    end_idx += 1;
                }
                let records = Self::index_records(tags, body_start, end_idx);
                sections.push(DxfRawSection {
                    name,
                    start_tag_index: start_idx,
                    end_tag_index: end_idx,
                    records,
                });
                i = end_idx + 1;
            } else {
                i += 1;
            }
        }
        sections
    }

    fn index_records(tags: &[DxfTag], start: usize, end: usize) -> Vec<DxfRawRecord> {
        let mut records = Vec::new();
        let mut i = start;
        while i < end {
            if tags[i].code == 0 {
                let rec_start = i;
                let name = tags[i].value.as_str().to_ascii_uppercase();
                let mut rec_end = i + 1;
                while rec_end < end && tags[rec_end].code != 0 {
                    rec_end += 1;
                }
                records.push(DxfRawRecord {
                    name,
                    start_tag_index: rec_start,
                    end_tag_index: rec_end,
                    tags: tags[rec_start..rec_end].to_vec(),
                });
                i = rec_end;
            } else {
                i += 1;
            }
        }
        records
    }

    /// Returns a new `DxfRawDocument` with `record` replaced by `replacement_tags`.
    pub fn with_record(&self, record: &DxfRawRecord, replacement_tags: Vec<DxfTag>) -> Self {
        let mut next = Vec::with_capacity(
            self.tags.len() - (record.end_tag_index - record.start_tag_index)
                + replacement_tags.len(),
        );
        next.extend_from_slice(&self.tags[..record.start_tag_index]);
        next.extend(replacement_tags);
        next.extend_from_slice(&self.tags[record.end_tag_index..]);
        Self::create(next, self.is_binary)
    }

    /// Returns a new `DxfRawDocument` with `record` removed.
    pub fn without_record(&self, record: &DxfRawRecord) -> Self {
        self.with_record(record, Vec::new())
    }

    /// Returns a new `DxfRawDocument` with a completely new tag stream.
    pub fn with_tags(&self, tags: Vec<DxfTag>) -> Self {
        Self::create(tags, self.is_binary)
    }

    /// Atomically saves the raw document to a file path.
    pub fn save(&self, path: impl AsRef<Path>, is_binary: bool) -> Result<()> {
        let bytes = self.save_to_bytes(is_binary)?;
        DxfAtomicFile::write_all(path, &bytes)
    }

    /// Serializes the raw document to an in-memory byte buffer.
    pub fn save_to_bytes(&self, is_binary: bool) -> Result<Vec<u8>> {
        let mut out = Vec::with_capacity(self.tags.len() * 12);
        self.save_to_writer(&mut out, is_binary)?;
        Ok(out)
    }

    /// Serializes the raw document to any writer.
    pub fn save_to_writer<W: Write>(&self, writer: W, is_binary: bool) -> Result<()> {
        if is_binary {
            let mut bw = BinaryCodeValueWriter::new(writer, false)?;
            Self::write_tags(&self.tags, &mut bw, true)?;
            bw.flush()
        } else {
            let mut tw = TextCodeValueWriter::new(writer);
            Self::write_tags(&self.tags, &mut tw, false)?;
            tw.flush()
        }
    }

    fn write_tags(tags: &[DxfTag], w: &mut dyn ICodeValueWriter, is_binary: bool) -> Result<()> {
        for tag in tags {
            if is_binary && tag.code == 999 {
                continue;
            }
            match &tag.value {
                CodeValue::String(s) => w.write_str(tag.code, s)?,
                CodeValue::Double(d) => w.write_f64(tag.code, *d)?,
                CodeValue::Int16(n) => w.write_i16(tag.code, *n)?,
                CodeValue::Int32(n) => w.write_i32(tag.code, *n)?,
                CodeValue::Int64(n) => w.write_i64(tag.code, *n)?,
                CodeValue::Bool(b) => w.write_bool(tag.code, *b)?,
                CodeValue::Byte(b) => w.write_byte(tag.code, *b)?,
                CodeValue::Bytes(b) => w.write_bytes(tag.code, b)?,
                CodeValue::None => {}
            }
        }
        Ok(())
    }
}
