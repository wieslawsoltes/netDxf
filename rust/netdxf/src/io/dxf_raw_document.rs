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

    pub const MAXIMUM_RAW_LW_POLYLINE_VERTICES: usize = 1_000_000;

    /// Returns the `$DWGCODEPAGE` value if present in the `HEADER` section.
    pub fn encoding_code_page(&self) -> Option<String> {
        let mut saw_var = false;
        for tag in &self.tags {
            if tag.code == 9 {
                saw_var = tag.value.as_str().eq_ignore_ascii_case("$DWGCODEPAGE");
            } else if saw_var && tag.code == 3 {
                return Some(tag.value.as_str().to_string());
            }
        }
        None
    }

    /// Returns `true` if the raw document contains at least one tag.
    pub fn has_original_bytes(&self) -> bool {
        !self.tags.is_empty()
    }

    /// Finds a section by name (case-insensitive).
    pub fn find_section(&self, name: &str) -> Option<&DxfRawSection> {
        self.sections
            .iter()
            .find(|s| s.name.eq_ignore_ascii_case(name))
    }

    /// Finds a record across all sections by its DXF handle (group code 5 or 105).
    pub fn find_record_by_handle(&self, handle: &str) -> Option<&DxfRawRecord> {
        let target = handle.trim();
        for section in &self.sections {
            for rec in &section.records {
                if rec.tags.iter().any(|t| {
                    (t.code == 5 || t.code == 105)
                        && t.value.as_str().trim().eq_ignore_ascii_case(target)
                }) {
                    return Some(rec);
                }
            }
        }
        None
    }

    /// Inserts a vertex `(x, y, bulge)` into a raw `LWPOLYLINE` record at `vertex_index`,
    /// updating the group-90 vertex count tag in place and returning a new `DxfRawDocument`.
    pub fn insert_lw_polyline_vertex(
        &self,
        record: &DxfRawRecord,
        vertex_index: usize,
        x: f64,
        y: f64,
        bulge: f64,
    ) -> Result<Self> {
        if !record.name.eq_ignore_ascii_case("LWPOLYLINE") {
            return Err(DxfError::InvalidValue(
                "Record is not an LWPOLYLINE".to_string(),
            ));
        }
        let mut new_tags = record.tags.clone();
        let mut vertex_positions = Vec::new();
        let mut count_idx = None;
        for (i, t) in new_tags.iter().enumerate() {
            if t.code == 90 && count_idx.is_none() {
                count_idx = Some(i);
            } else if t.code == 10 {
                vertex_positions.push(i);
            }
        }
        if vertex_positions.len() >= Self::MAXIMUM_RAW_LW_POLYLINE_VERTICES {
            return Err(DxfError::InvalidValue(
                "LWPOLYLINE vertex count exceeds maximum limit".to_string(),
            ));
        }
        let insert_at = if vertex_index < vertex_positions.len() {
            vertex_positions[vertex_index]
        } else {
            new_tags.len()
        };
        let mut v_tags = vec![
            DxfTag::new(10, CodeValue::Double(x)),
            DxfTag::new(20, CodeValue::Double(y)),
        ];
        if bulge != 0.0 {
            v_tags.push(DxfTag::new(42, CodeValue::Double(bulge)));
        }
        new_tags.splice(insert_at..insert_at, v_tags);
        if let Some(ci) = count_idx {
            let new_count = (vertex_positions.len() + 1) as i32;
            new_tags[ci] = DxfTag::new(90, CodeValue::Int32(new_count));
        }
        Ok(self.with_record(record, new_tags))
    }

    /// Atomically saves the raw document to a file path.
    pub fn save_atomic(&self, path: impl AsRef<Path>, is_binary: bool) -> Result<()> {
        self.save(path, is_binary)
    }

    /// Reads raw `POINT` geometry from a record.
    pub fn read_point_geometry(record: &DxfRawRecord) -> Option<crate::io::DxfRawPointGeometry> {
        if !record.name.eq_ignore_ascii_case("POINT") {
            return None;
        }
        let mut g = crate::io::DxfRawPointGeometry {
            extrusion: crate::Vector3::UNIT_Z,
            ..Default::default()
        };
        for t in &record.tags {
            match t.code {
                10 => g.position.x = t.value.as_f64(),
                20 => g.position.y = t.value.as_f64(),
                30 => g.position.z = t.value.as_f64(),
                39 => g.thickness = t.value.as_f64(),
                50 => g.angle = t.value.as_f64(),
                210 => g.extrusion.x = t.value.as_f64(),
                220 => g.extrusion.y = t.value.as_f64(),
                230 => g.extrusion.z = t.value.as_f64(),
                _ => {}
            }
        }
        Some(g)
    }

    /// Reads raw `LINE` geometry from a record.
    pub fn read_line_geometry(record: &DxfRawRecord) -> Option<crate::io::DxfRawLineGeometry> {
        if !record.name.eq_ignore_ascii_case("LINE") {
            return None;
        }
        let mut g = crate::io::DxfRawLineGeometry {
            extrusion: crate::Vector3::UNIT_Z,
            ..Default::default()
        };
        for t in &record.tags {
            match t.code {
                10 => g.start.x = t.value.as_f64(),
                20 => g.start.y = t.value.as_f64(),
                30 => g.start.z = t.value.as_f64(),
                11 => g.end.x = t.value.as_f64(),
                21 => g.end.y = t.value.as_f64(),
                31 => g.end.z = t.value.as_f64(),
                39 => g.thickness = t.value.as_f64(),
                210 => g.extrusion.x = t.value.as_f64(),
                220 => g.extrusion.y = t.value.as_f64(),
                230 => g.extrusion.z = t.value.as_f64(),
                _ => {}
            }
        }
        Some(g)
    }

    /// Reads raw `CIRCLE` geometry from a record.
    pub fn read_circle_geometry(record: &DxfRawRecord) -> Option<crate::io::DxfRawCircleGeometry> {
        if !record.name.eq_ignore_ascii_case("CIRCLE") {
            return None;
        }
        let mut g = crate::io::DxfRawCircleGeometry {
            extrusion: crate::Vector3::UNIT_Z,
            ..Default::default()
        };
        for t in &record.tags {
            match t.code {
                10 => g.center.x = t.value.as_f64(),
                20 => g.center.y = t.value.as_f64(),
                30 => g.center.z = t.value.as_f64(),
                40 => g.radius = t.value.as_f64(),
                39 => g.thickness = t.value.as_f64(),
                210 => g.extrusion.x = t.value.as_f64(),
                220 => g.extrusion.y = t.value.as_f64(),
                230 => g.extrusion.z = t.value.as_f64(),
                _ => {}
            }
        }
        Some(g)
    }

    /// Reads raw `ARC` geometry from a record.
    pub fn read_arc_geometry(record: &DxfRawRecord) -> Option<crate::io::DxfRawArcGeometry> {
        if !record.name.eq_ignore_ascii_case("ARC") {
            return None;
        }
        let mut g = crate::io::DxfRawArcGeometry {
            extrusion: crate::Vector3::UNIT_Z,
            ..Default::default()
        };
        for t in &record.tags {
            match t.code {
                10 => g.center.x = t.value.as_f64(),
                20 => g.center.y = t.value.as_f64(),
                30 => g.center.z = t.value.as_f64(),
                40 => g.radius = t.value.as_f64(),
                50 => g.start_angle = t.value.as_f64(),
                51 => g.end_angle = t.value.as_f64(),
                39 => g.thickness = t.value.as_f64(),
                210 => g.extrusion.x = t.value.as_f64(),
                220 => g.extrusion.y = t.value.as_f64(),
                230 => g.extrusion.z = t.value.as_f64(),
                _ => {}
            }
        }
        Some(g)
    }

    /// Reads raw `ELLIPSE` geometry from a record.
    pub fn read_ellipse_geometry(record: &DxfRawRecord) -> Option<crate::io::DxfRawEllipseGeometry> {
        if !record.name.eq_ignore_ascii_case("ELLIPSE") {
            return None;
        }
        let mut g = crate::io::DxfRawEllipseGeometry {
            extrusion: crate::Vector3::UNIT_Z,
            minor_axis_ratio: 1.0,
            end_parameter: std::f64::consts::TAU,
            ..Default::default()
        };
        for t in &record.tags {
            match t.code {
                10 => g.center.x = t.value.as_f64(),
                20 => g.center.y = t.value.as_f64(),
                30 => g.center.z = t.value.as_f64(),
                11 => g.major_axis_endpoint.x = t.value.as_f64(),
                21 => g.major_axis_endpoint.y = t.value.as_f64(),
                31 => g.major_axis_endpoint.z = t.value.as_f64(),
                40 => g.minor_axis_ratio = t.value.as_f64(),
                41 => g.start_parameter = t.value.as_f64(),
                42 => g.end_parameter = t.value.as_f64(),
                210 => g.extrusion.x = t.value.as_f64(),
                220 => g.extrusion.y = t.value.as_f64(),
                230 => g.extrusion.z = t.value.as_f64(),
                _ => {}
            }
        }
        Some(g)
    }

    /// Reads raw `LWPOLYLINE` geometry from a record.
    pub fn read_lw_polyline_geometry(
        record: &DxfRawRecord,
    ) -> Option<crate::io::DxfRawLwPolylineGeometry> {
        if !record.name.eq_ignore_ascii_case("LWPOLYLINE") {
            return None;
        }
        let mut g = crate::io::DxfRawLwPolylineGeometry {
            extrusion: crate::Vector3::UNIT_Z,
            ..Default::default()
        };
        let mut cur: Option<crate::io::DxfRawLwPolylineVertex> = None;
        for t in &record.tags {
            match t.code {
                70 => g.is_closed = (t.value.as_i16() & 1) != 0,
                38 => g.elevation = t.value.as_f64(),
                39 => g.thickness = t.value.as_f64(),
                43 => g.constant_width = t.value.as_f64(),
                210 => g.extrusion.x = t.value.as_f64(),
                220 => g.extrusion.y = t.value.as_f64(),
                230 => g.extrusion.z = t.value.as_f64(),
                10 => {
                    if let Some(v) = cur.take() {
                        g.vertices.push(v);
                    }
                    cur = Some(crate::io::DxfRawLwPolylineVertex {
                        position: crate::Vector2::new(t.value.as_f64(), 0.0),
                        ..Default::default()
                    });
                }
                20 => {
                    if let Some(v) = cur.as_mut() {
                        v.position.y = t.value.as_f64();
                    }
                }
                40 => {
                    if let Some(v) = cur.as_mut() {
                        v.start_width = t.value.as_f64();
                    }
                }
                41 => {
                    if let Some(v) = cur.as_mut() {
                        v.end_width = t.value.as_f64();
                    }
                }
                42 => {
                    if let Some(v) = cur.as_mut() {
                        v.bulge = t.value.as_f64();
                    }
                }
                _ => {}
            }
        }
        if let Some(v) = cur {
            g.vertices.push(v);
        }
        Some(g)
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
