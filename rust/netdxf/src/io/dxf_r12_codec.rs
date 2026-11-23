use std::io::{BufReader, Cursor, Read, Seek, Write};

use encoding_rs::WINDOWS_1252;

use crate::dxf_document::DxfDocument;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::*;
use crate::error::Result;
use crate::header::DxfVersion;
use crate::io::code_value_reader::ICodeValueReader;
use crate::io::code_value_writer::ICodeValueWriter;
use crate::io::text_code_value_reader::TextCodeValueReader;
use crate::io::text_code_value_writer::TextCodeValueWriter;
use crate::tables::TableObject;
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// AutoCAD R12 (`AC1009`) DXF reader and writer (`netDxf.IO.DxfR12Codec`).
///
/// Supports reading and exporting R12-compatible DXF drawings without subclass markers (`100`)
/// or modern `OBJECTS` / `CLASSES` sections, converting `Polyline2D` to R12 `POLYLINE`/`VERTEX`/`SEQEND`.
pub struct DxfR12Codec;

impl DxfR12Codec {
    pub fn create() -> Self {
        Self
    }

    pub fn create_with_code_page(_code_page: &str) -> Self {
        Self
    }

    pub fn save(doc: &DxfDocument, path: impl AsRef<std::path::Path>) -> Result<()> {
        let bytes = Self::write_to_bytes(doc)?;
        std::fs::write(path, bytes)?;
        Ok(())
    }

    pub fn save_with_code_page(
        doc: &DxfDocument,
        path: impl AsRef<std::path::Path>,
        _code_page: &str,
    ) -> Result<()> {
        Self::save(doc, path)
    }

    pub fn read_entities(bytes: &[u8]) -> Result<Vec<EntityObject>> {
        let doc = Self::read_from_bytes(bytes)?;
        Ok(doc.entities.iter().cloned().collect())
    }

    /// Exports a `DxfDocument` as an AutoCAD R12 (`AC1009`) ASCII DXF byte buffer.
    pub fn write_to_bytes(doc: &DxfDocument) -> Result<Vec<u8>> {
        let mut out = Vec::with_capacity(8192);
        Self::write(doc, &mut out)?;
        Ok(out)
    }

    /// Exports a `DxfDocument` as an AutoCAD R12 (`AC1009`) ASCII DXF stream.
    pub fn write<W: Write>(doc: &DxfDocument, writer: W) -> Result<()> {
        let mut w = TextCodeValueWriter::new(writer);
        // HEADER
        w.write_str(0, DxfObjectCode::BEGIN_SECTION)?;
        w.write_str(2, DxfObjectCode::HEADER_SECTION)?;
        w.write_str(9, "$ACADVER")?;
        w.write_str(1, "AC1009")?;
        w.write_str(9, "$INSBASE")?;
        w.write_f64(10, doc.drawing_variables.ins_base.x)?;
        w.write_f64(20, doc.drawing_variables.ins_base.y)?;
        w.write_f64(30, doc.drawing_variables.ins_base.z)?;
        w.write_str(0, DxfObjectCode::END_SECTION)?;

        // TABLES (LTYPE, LAYER, STYLE)
        w.write_str(0, DxfObjectCode::BEGIN_SECTION)?;
        w.write_str(2, DxfObjectCode::TABLES_SECTION)?;
        w.write_str(0, DxfObjectCode::TABLE)?;
        w.write_str(2, DxfObjectCode::LAYER_TABLE)?;
        w.write_i16(70, doc.layers.len() as i16)?;
        for layer in doc.layers.iter() {
            w.write_str(0, DxfObjectCode::LAYER)?;
            w.write_str(2, layer.name())?;
            w.write_i16(70, layer.flags().bits())?;
            let color_idx = if layer.is_visible {
                layer.color.index.abs()
            } else {
                -layer.color.index.abs()
            };
            w.write_i16(62, color_idx)?;
            w.write_str(6, &layer.linetype)?;
        }
        w.write_str(0, DxfObjectCode::END_TABLE)?;
        w.write_str(0, DxfObjectCode::END_SECTION)?;

        // BLOCKS
        w.write_str(0, DxfObjectCode::BEGIN_SECTION)?;
        w.write_str(2, DxfObjectCode::BLOCKS_SECTION)?;
        for blk in doc.blocks.iter() {
            if blk.name().starts_with('*') {
                continue;
            }
            w.write_str(0, DxfObjectCode::BEGIN_BLOCK)?;
            w.write_str(8, &blk.layer)?;
            w.write_str(2, blk.name())?;
            w.write_i16(70, blk.flags.bits())?;
            w.write_f64(10, blk.origin.x)?;
            w.write_f64(20, blk.origin.y)?;
            w.write_f64(30, blk.origin.z)?;
            w.write_str(3, blk.name())?;
            for ent in &blk.entities {
                Self::write_r12_entity(ent, &mut w)?;
            }
            w.write_str(0, DxfObjectCode::END_BLOCK)?;
            w.write_str(8, &blk.layer)?;
        }
        w.write_str(0, DxfObjectCode::END_SECTION)?;

        // ENTITIES
        w.write_str(0, DxfObjectCode::BEGIN_SECTION)?;
        w.write_str(2, DxfObjectCode::ENTITIES_SECTION)?;
        for ent in doc.entities.iter() {
            Self::write_r12_entity(ent, &mut w)?;
        }
        w.write_str(0, DxfObjectCode::END_SECTION)?;
        w.write_str(0, DxfObjectCode::END_OF_FILE)?;
        w.flush()
    }

    fn write_r12_common(c: &EntityCommon, w: &mut dyn ICodeValueWriter) -> Result<()> {
        w.write_str(8, &c.layer)?;
        if !c.color.is_by_layer() {
            w.write_i16(62, c.color.index)?;
        }
        Ok(())
    }

    fn write_r12_entity(ent: &EntityObject, w: &mut dyn ICodeValueWriter) -> Result<()> {
        match ent {
            EntityObject::Point(p) => {
                w.write_str(0, DxfObjectCode::POINT)?;
                Self::write_r12_common(&p.common, w)?;
                w.write_f64(10, p.position.x)?;
                w.write_f64(20, p.position.y)?;
                w.write_f64(30, p.position.z)?;
            }
            EntityObject::Line(l) => {
                w.write_str(0, DxfObjectCode::LINE)?;
                Self::write_r12_common(&l.common, w)?;
                w.write_f64(10, l.start_point.x)?;
                w.write_f64(20, l.start_point.y)?;
                w.write_f64(30, l.start_point.z)?;
                w.write_f64(11, l.end_point.x)?;
                w.write_f64(21, l.end_point.y)?;
                w.write_f64(31, l.end_point.z)?;
            }
            EntityObject::Circle(c) => {
                w.write_str(0, DxfObjectCode::CIRCLE)?;
                Self::write_r12_common(&c.common, w)?;
                w.write_f64(10, c.center.x)?;
                w.write_f64(20, c.center.y)?;
                w.write_f64(30, c.center.z)?;
                w.write_f64(40, c.radius)?;
            }
            EntityObject::Arc(a) => {
                w.write_str(0, DxfObjectCode::ARC)?;
                Self::write_r12_common(&a.common, w)?;
                w.write_f64(10, a.center.x)?;
                w.write_f64(20, a.center.y)?;
                w.write_f64(30, a.center.z)?;
                w.write_f64(40, a.radius)?;
                w.write_f64(50, a.start_angle)?;
                w.write_f64(51, a.end_angle)?;
            }
            EntityObject::Text(t) => {
                w.write_str(0, DxfObjectCode::TEXT)?;
                Self::write_r12_common(&t.common, w)?;
                w.write_f64(10, t.position.x)?;
                w.write_f64(20, t.position.y)?;
                w.write_f64(30, t.position.z)?;
                w.write_f64(40, t.height)?;
                w.write_str(1, &t.value)?;
                if t.rotation != 0.0 {
                    w.write_f64(50, t.rotation)?;
                }
            }
            EntityObject::Face3D(f) => {
                w.write_str(0, DxfObjectCode::FACE_3D)?;
                Self::write_r12_common(&f.common, w)?;
                w.write_f64(10, f.first_vertex.x)?;
                w.write_f64(20, f.first_vertex.y)?;
                w.write_f64(30, f.first_vertex.z)?;
                w.write_f64(11, f.second_vertex.x)?;
                w.write_f64(21, f.second_vertex.y)?;
                w.write_f64(31, f.second_vertex.z)?;
                w.write_f64(12, f.third_vertex.x)?;
                w.write_f64(22, f.third_vertex.y)?;
                w.write_f64(32, f.third_vertex.z)?;
                w.write_f64(13, f.fourth_vertex.x)?;
                w.write_f64(23, f.fourth_vertex.y)?;
                w.write_f64(33, f.fourth_vertex.z)?;
                w.write_i16(70, f.edge_flags.bits())?;
            }
            EntityObject::Polyline2D(pl) => {
                w.write_str(0, DxfObjectCode::POLYLINE)?;
                Self::write_r12_common(&pl.common, w)?;
                w.write_i16(66, 1)?;
                w.write_f64(10, 0.0)?;
                w.write_f64(20, 0.0)?;
                w.write_f64(30, pl.elevation)?;
                w.write_i16(70, if pl.is_closed { 1 } else { 0 })?;
                for v in &pl.vertexes {
                    w.write_str(0, DxfObjectCode::VERTEX)?;
                    w.write_str(8, &pl.common.layer)?;
                    w.write_f64(10, v.position.x)?;
                    w.write_f64(20, v.position.y)?;
                    w.write_f64(30, pl.elevation)?;
                    if v.bulge != 0.0 {
                        w.write_f64(42, v.bulge)?;
                    }
                }
                w.write_str(0, DxfObjectCode::END_SEQUENCE)?;
                w.write_str(8, &pl.common.layer)?;
            }
            EntityObject::Polyline3D(pl) => {
                w.write_str(0, DxfObjectCode::POLYLINE)?;
                Self::write_r12_common(&pl.common, w)?;
                w.write_i16(66, 1)?;
                w.write_i16(70, 8 | if pl.is_closed { 1 } else { 0 })?;
                for v in &pl.vertexes {
                    w.write_str(0, DxfObjectCode::VERTEX)?;
                    w.write_str(8, &pl.common.layer)?;
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, v.z)?;
                    w.write_i16(70, 32)?;
                }
                w.write_str(0, DxfObjectCode::END_SEQUENCE)?;
                w.write_str(8, &pl.common.layer)?;
            }
            EntityObject::Insert(ins) => {
                w.write_str(0, DxfObjectCode::INSERT)?;
                Self::write_r12_common(&ins.common, w)?;
                w.write_str(2, &ins.block_name)?;
                w.write_f64(10, ins.position.x)?;
                w.write_f64(20, ins.position.y)?;
                w.write_f64(30, ins.position.z)?;
                w.write_f64(41, ins.scale.x)?;
                w.write_f64(42, ins.scale.y)?;
                w.write_f64(43, ins.scale.z)?;
                w.write_f64(50, ins.rotation)?;
            }
            _ => {}
        }
        Ok(())
    }

    /// Reads an AutoCAD R12 (`AC1009`) ASCII DXF byte slice into a `DxfDocument` (upgraded to `AutoCad2000` by default).
    pub fn read_from_bytes(bytes: &[u8]) -> Result<DxfDocument> {
        Self::read(Cursor::new(bytes))
    }

    /// Reads an AutoCAD R12 (`AC1009`) ASCII DXF stream into a `DxfDocument`.
    pub fn read<R: Read + Seek>(stream: R) -> Result<DxfDocument> {
        let mut reader = TextCodeValueReader::new(BufReader::new(stream), WINDOWS_1252);
        reader.set_skip_comments(true);
        let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2000);
        let mut in_entities = false;

        if !reader.next()? {
            return Ok(doc);
        }

        while !reader.is_eof() {
            if reader.code() == 0 {
                let name = reader.value().as_str().to_ascii_uppercase();
                match name.as_str() {
                    DxfObjectCode::END_OF_FILE => break,
                    DxfObjectCode::BEGIN_SECTION => {
                        if reader.next()? && reader.code() == 2 {
                            in_entities = reader
                                .value()
                                .as_str()
                                .eq_ignore_ascii_case(DxfObjectCode::ENTITIES_SECTION);
                        }
                    }
                    DxfObjectCode::END_SECTION => in_entities = false,
                    DxfObjectCode::LINE if in_entities => {
                        let mut line = Line::new(Vector3::ZERO, Vector3::ZERO);
                        while reader.next()? && reader.code() != 0 {
                            match reader.code() {
                                8 => line.common.layer = reader.value().as_str().to_string(),
                                10 => line.start_point.x = reader.value().as_f64(),
                                20 => line.start_point.y = reader.value().as_f64(),
                                30 => line.start_point.z = reader.value().as_f64(),
                                11 => line.end_point.x = reader.value().as_f64(),
                                21 => line.end_point.y = reader.value().as_f64(),
                                31 => line.end_point.z = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        doc.add_entity(line);
                        continue;
                    }
                    DxfObjectCode::CIRCLE if in_entities => {
                        let mut c = Circle::new(Vector3::ZERO, 1.0);
                        while reader.next()? && reader.code() != 0 {
                            match reader.code() {
                                8 => c.common.layer = reader.value().as_str().to_string(),
                                10 => c.center.x = reader.value().as_f64(),
                                20 => c.center.y = reader.value().as_f64(),
                                30 => c.center.z = reader.value().as_f64(),
                                40 => c.radius = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        doc.add_entity(c);
                        continue;
                    }
                    DxfObjectCode::ARC if in_entities => {
                        let mut a = Arc::new(Vector3::ZERO, 1.0, 0.0, 90.0);
                        while reader.next()? && reader.code() != 0 {
                            match reader.code() {
                                8 => a.common.layer = reader.value().as_str().to_string(),
                                10 => a.center.x = reader.value().as_f64(),
                                20 => a.center.y = reader.value().as_f64(),
                                30 => a.center.z = reader.value().as_f64(),
                                40 => a.radius = reader.value().as_f64(),
                                50 => a.start_angle = reader.value().as_f64(),
                                51 => a.end_angle = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        doc.add_entity(a);
                        continue;
                    }
                    DxfObjectCode::POINT if in_entities => {
                        let mut p = Point::new(Vector3::ZERO);
                        while reader.next()? && reader.code() != 0 {
                            match reader.code() {
                                8 => p.common.layer = reader.value().as_str().to_string(),
                                10 => p.position.x = reader.value().as_f64(),
                                20 => p.position.y = reader.value().as_f64(),
                                30 => p.position.z = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        doc.add_entity(p);
                        continue;
                    }
                    DxfObjectCode::TEXT if in_entities => {
                        let mut t = Text::new("", Vector3::ZERO, 1.0);
                        while reader.next()? && reader.code() != 0 {
                            match reader.code() {
                                8 => t.common.layer = reader.value().as_str().to_string(),
                                1 => t.value = reader.value().as_str().to_string(),
                                10 => t.position.x = reader.value().as_f64(),
                                20 => t.position.y = reader.value().as_f64(),
                                30 => t.position.z = reader.value().as_f64(),
                                40 => t.height = reader.value().as_f64(),
                                50 => t.rotation = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        doc.add_entity(t);
                        continue;
                    }
                    DxfObjectCode::POLYLINE if in_entities => {
                        let mut layer = "0".to_string();
                        let mut flags: i16 = 0;
                        let mut elevation = 0.0;
                        while reader.next()? && reader.code() != 0 {
                            match reader.code() {
                                8 => layer = reader.value().as_str().to_string(),
                                30 => elevation = reader.value().as_f64(),
                                70 => flags = reader.value().as_i16(),
                                _ => {}
                            }
                        }
                        let is_3d = (flags & 8) != 0;
                        let is_closed = (flags & 1) != 0;
                        let mut verts_2d = Vec::new();
                        let mut verts_3d = Vec::new();
                        while !reader.is_eof() && reader.code() == 0 {
                            let sub = reader.value().as_str().to_ascii_uppercase();
                            if sub == DxfObjectCode::END_SEQUENCE {
                                while reader.next()? && reader.code() != 0 {}
                                break;
                            } else if sub == DxfObjectCode::VERTEX {
                                let mut vx = 0.0;
                                let mut vy = 0.0;
                                let mut vz = elevation;
                                let mut bulge = 0.0;
                                while reader.next()? && reader.code() != 0 {
                                    match reader.code() {
                                        10 => vx = reader.value().as_f64(),
                                        20 => vy = reader.value().as_f64(),
                                        30 => vz = reader.value().as_f64(),
                                        42 => bulge = reader.value().as_f64(),
                                        _ => {}
                                    }
                                }
                                if is_3d {
                                    verts_3d.push(Vector3::new(vx, vy, vz));
                                } else {
                                    verts_2d.push(Polyline2DVertex {
                                        position: Vector2::new(vx, vy),
                                        bulge,
                                        ..Polyline2DVertex::new(vx, vy)
                                    });
                                }
                            } else {
                                break;
                            }
                        }
                        if is_3d {
                            let mut pl = Polyline3D::new(verts_3d, is_closed);
                            pl.common.layer = layer;
                            doc.add_entity(pl);
                        } else {
                            let mut pl = Polyline2D::new(verts_2d, is_closed);
                            pl.common.layer = layer;
                            pl.elevation = elevation;
                            doc.add_entity(pl);
                        }
                        continue;
                    }
                    _ => {}
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(doc)
    }
}
