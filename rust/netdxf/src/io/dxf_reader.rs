use std::io::{BufReader, Read, Seek, SeekFrom};

use encoding_rs::{Encoding, UTF_8, WINDOWS_1252};

use crate::aci_color::AciColor;
use crate::blocks::{Block, BlockTypeFlags};
use crate::clipping_boundary::ClippingBoundary;
use crate::clipping_boundary_type::ClippingBoundaryType;
use crate::dxf_class::DxfClass;
use crate::dxf_document::DxfDocument;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::*;
use crate::error::{DxfError, Result};
use crate::header::{AttMode, DxfVersion, HeaderVariable, HeaderVariableCode, HeaderVariableValue, PointShape};
use crate::io::binary_code_value_reader::{BinaryCodeValueReader, BINARY_DXF_SENTINEL};
use crate::io::code_value_reader::{decode_hex, ICodeValueReader};
use crate::io::text_code_value_reader::TextCodeValueReader;
use crate::lineweight::Lineweight;
use crate::math_helper::MathHelper;
use crate::objects::*;
use crate::tables::*;
use crate::transparency::Transparency;
use crate::units::*;
use crate::vector2::Vector2;
use crate::vector3::Vector3;
use crate::x_data::XData;
use crate::x_data_code::XDataCode;
use crate::x_data_record::{XDataRecord, XDataValue};

/// High-performance DXF file reader supporting ASCII and Binary formats (`netDxf.IO.DxfReader`).
pub struct DxfReader;

impl DxfReader {
    /// Checks whether a stream is binary DXF and extracts `$ACADVER`.
    /// Always restores the stream position to its original offset.
    pub fn check_version<R: Read + Seek>(mut stream: R) -> Result<(DxfVersion, bool)> {
        let start_pos = stream.stream_position()?;
        let mut sentinel = [0u8; 22];
        let is_binary = match stream.read_exact(&mut sentinel) {
            Ok(()) => &sentinel == BINARY_DXF_SENTINEL,
            Err(_) => false,
        };
        stream.seek(SeekFrom::Start(start_pos))?;

        let version_res = {
            let buf = BufReader::new(&mut stream);
            if is_binary {
                match BinaryCodeValueReader::new(buf, WINDOWS_1252, false) {
                    Ok(mut reader) => {
                        reader.set_skip_comments(true);
                        Self::scan_version(&mut reader)
                    }
                    Err(_) => Ok(DxfVersion::Unknown),
                }
            } else {
                let mut reader = TextCodeValueReader::new(buf, WINDOWS_1252);
                reader.set_skip_comments(true);
                Self::scan_version(&mut reader)
            }
        };

        stream.seek(SeekFrom::Start(start_pos))?;
        match version_res {
            Ok(version) => Ok((version, is_binary)),
            Err(_) => Ok((DxfVersion::Unknown, false)),
        }
    }

    fn scan_version(reader: &mut dyn ICodeValueReader) -> Result<DxfVersion> {
        let mut version = DxfVersion::Unknown;
        let mut in_section = false;
        let mut in_header = false;
        let mut expect_section_name = false;
        while reader.next()? {
            let code = reader.code();
            if expect_section_name {
                expect_section_name = false;
                if code == 2 {
                    in_section = true;
                    in_header = reader
                        .value()
                        .as_str()
                        .eq_ignore_ascii_case(DxfObjectCode::HEADER_SECTION);
                } else {
                    in_section = false;
                    in_header = false;
                }
                continue;
            }
            if code == 0 {
                let s = reader.value().as_str();
                if s.eq_ignore_ascii_case(DxfObjectCode::BEGIN_SECTION) && !in_section {
                    expect_section_name = true;
                } else if s.eq_ignore_ascii_case(DxfObjectCode::END_SECTION) {
                    if in_header {
                        break;
                    }
                    in_section = false;
                    in_header = false;
                } else if s.eq_ignore_ascii_case(DxfObjectCode::END_OF_FILE) {
                    break;
                }
            } else if in_header
                && code == 9
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(HeaderVariableCode::ACAD_VER)
            {
                if reader.next()? && reader.code() == 1 {
                    version = DxfVersion::from_code(reader.value().as_str());
                }
                break;
            }
        }
        Ok(version)
    }

    /// Reads an entire DXF document from any seekable reader (ASCII or Binary).
    pub fn read<R: Read + Seek>(mut stream: R) -> Result<DxfDocument> {
        let (version, is_binary) = Self::check_version(&mut stream)?;
        let encoding: &'static Encoding = if version.uses_utf8() { UTF_8 } else { WINDOWS_1252 };

        let buf = BufReader::with_capacity(64 * 1024, stream);
        if is_binary {
            let mut reader = BinaryCodeValueReader::new(buf, encoding, false)?;
            Self::read_document(&mut reader)
        } else {
            let mut reader = TextCodeValueReader::new(buf, encoding);
            Self::read_document(&mut reader)
        }
    }

    fn read_document(reader: &mut dyn ICodeValueReader) -> Result<DxfDocument> {
        let mut doc = DxfDocument::new();
        doc.comments.clear();

        if !reader.next()? {
            return Err(DxfError::UnexpectedEof);
        }

        while !reader.is_eof() {
            let code = reader.code();
            if code == 999 {
                doc.comments.push(reader.value().as_str().to_string());
                if !reader.next()? {
                    break;
                }
                continue;
            }

            if code == 0 {
                let val = reader.value().as_str().to_ascii_uppercase();
                if val == DxfObjectCode::END_OF_FILE {
                    break;
                }
                if val == DxfObjectCode::BEGIN_SECTION {
                    reader.set_skip_comments(true);
                    if !reader.next()? {
                        break;
                    }
                    if reader.code() == 2 {
                        let section = reader.value().as_str().to_ascii_uppercase();
                        match section.as_str() {
                            DxfObjectCode::HEADER_SECTION => Self::read_header(reader, &mut doc)?,
                            DxfObjectCode::CLASSES_SECTION => Self::read_classes(reader, &mut doc)?,
                            DxfObjectCode::TABLES_SECTION => Self::read_tables(reader, &mut doc)?,
                            DxfObjectCode::BLOCKS_SECTION => Self::read_blocks(reader, &mut doc)?,
                            DxfObjectCode::ENTITIES_SECTION => Self::read_entities(reader, &mut doc)?,
                            DxfObjectCode::OBJECTS_SECTION => Self::read_objects(reader, &mut doc)?,
                            DxfObjectCode::THUMBNAIL_IMAGE_SECTION => {
                                Self::read_thumbnail(reader, &mut doc)?
                            }
                            _ => Self::skip_section(reader)?,
                        }
                    }
                }
            }

            if !reader.next()? {
                break;
            }
        }

        // Update num_handles from handle_seed if present
        if let Ok(seed) = u64::from_str_radix(doc.drawing_variables.handle_seed.trim(), 16) {
            if seed > doc.num_handles {
                doc.num_handles = seed;
            }
        }

        Ok(doc)
    }

    #[inline]
    fn at_section_end(reader: &dyn ICodeValueReader) -> bool {
        reader.is_eof()
            || (reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::END_SECTION))
    }

    fn skip_section(reader: &mut dyn ICodeValueReader) -> Result<()> {
        while reader.next()? {
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::END_SECTION)
            {
                return Ok(());
            }
        }
        Ok(())
    }

    fn read_header(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !Self::at_section_end(reader) {
            if reader.code() == 9 {
                let var_name = reader.value().as_str().to_ascii_uppercase();
                if !reader.next()? {
                    break;
                }
                let hv = &mut doc.drawing_variables;
                match var_name.as_str() {
                    HeaderVariableCode::ACAD_VER => {
                        hv.acad_ver = DxfVersion::from_code(reader.value().as_str());
                    }
                    HeaderVariableCode::DWG_CODE_PAGE => {
                        hv.dwg_code_page = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::LAST_SAVED_BY => {
                        hv.last_saved_by = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::HANDLE_SEED => {
                        hv.handle_seed = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::ANGBASE => hv.angbase = reader.value().as_f64(),
                    HeaderVariableCode::ANGDIR => {
                        if let Some(d) = AngleDirection::from_i16(reader.value().as_i16()) {
                            hv.angdir = d;
                        }
                    }
                    HeaderVariableCode::ATT_MODE => {
                        if let Some(m) = AttMode::from_i16(reader.value().as_i16()) {
                            hv.att_mode = m;
                        }
                    }
                    HeaderVariableCode::A_UNITS => {
                        if let Some(u) = AngleUnitType::from_i16(reader.value().as_i16()) {
                            hv.a_units = u;
                        }
                    }
                    HeaderVariableCode::A_UPREC => hv.a_uprec = reader.value().as_i16(),
                    HeaderVariableCode::CE_COLOR => {
                        hv.ce_color = AciColor::from_cad_index(reader.value().as_i16());
                    }
                    HeaderVariableCode::CE_LT_SCALE => hv.ce_lt_scale = reader.value().as_f64(),
                    HeaderVariableCode::CE_LTYPE => {
                        hv.ce_ltype = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::CE_LWEIGHT => {
                        if let Some(lw) = Lineweight::from_i16(reader.value().as_i16()) {
                            hv.ce_lweight = lw;
                        }
                    }
                    HeaderVariableCode::C_LAYER => {
                        hv.c_layer = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::CML_JUST => {
                        if let Some(j) = MLineJustification::from_i16(reader.value().as_i16()) {
                            hv.cml_just = j;
                        }
                    }
                    HeaderVariableCode::CML_SCALE => hv.cml_scale = reader.value().as_f64(),
                    HeaderVariableCode::CML_STYLE => {
                        hv.cml_style = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::DIM_STYLE => {
                        hv.dim_style = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::TEXT_SIZE => hv.text_size = reader.value().as_f64(),
                    HeaderVariableCode::TEXT_STYLE => {
                        hv.text_style = reader.value().as_str().to_string();
                    }
                    HeaderVariableCode::L_UNITS => {
                        if let Some(u) = LinearUnitType::from_i16(reader.value().as_i16()) {
                            hv.l_units = u;
                        }
                    }
                    HeaderVariableCode::L_UPREC => hv.l_uprec = reader.value().as_i16(),
                    HeaderVariableCode::MIRR_TEXT => hv.mirr_text = reader.value().as_bool(),
                    HeaderVariableCode::EXTNAMES => hv.extnames = reader.value().as_bool(),
                    HeaderVariableCode::INS_UNITS => {
                        if let Some(u) = DrawingUnits::from_i16(reader.value().as_i16()) {
                            hv.ins_units = u;
                        }
                    }
                    HeaderVariableCode::LT_SCALE => hv.lt_scale = reader.value().as_f64(),
                    HeaderVariableCode::LW_DISPLAY => hv.lw_display = reader.value().as_bool(),
                    HeaderVariableCode::PD_MODE => {
                        if let Some(p) = PointShape::from_i16(reader.value().as_i16()) {
                            hv.pd_mode = p;
                        }
                    }
                    HeaderVariableCode::PD_SIZE => hv.pd_size = reader.value().as_f64(),
                    HeaderVariableCode::P_LINE_GEN => hv.p_line_gen = reader.value().as_i16(),
                    HeaderVariableCode::PS_LT_SCALE => hv.ps_lt_scale = reader.value().as_i16(),
                    HeaderVariableCode::SPLINE_SEGS => hv.spline_segs = reader.value().as_i16(),
                    HeaderVariableCode::SURF_U => hv.surf_u = reader.value().as_i16(),
                    HeaderVariableCode::SURF_V => hv.surf_v = reader.value().as_i16(),
                    HeaderVariableCode::TD_CREATE => {
                        if let Ok(t) = DrawingTime::from_julian_calendar(reader.value().as_f64()) {
                            hv.td_create = t;
                        }
                    }
                    HeaderVariableCode::TDU_CREATE => {
                        if let Ok(t) = DrawingTime::from_julian_calendar(reader.value().as_f64()) {
                            hv.tdu_create = t;
                        }
                    }
                    HeaderVariableCode::TD_UPDATE => {
                        if let Ok(t) = DrawingTime::from_julian_calendar(reader.value().as_f64()) {
                            hv.td_update = t;
                        }
                    }
                    HeaderVariableCode::TDU_UPDATE => {
                        if let Ok(t) = DrawingTime::from_julian_calendar(reader.value().as_f64()) {
                            hv.tdu_update = t;
                        }
                    }
                    HeaderVariableCode::TDIN_DWG => {
                        if let Ok(d) = DrawingTime::editing_time(reader.value().as_f64()) {
                            hv.tdin_dwg = d;
                        }
                    }
                    HeaderVariableCode::INS_BASE
                    | HeaderVariableCode::UCS_ORG
                    | HeaderVariableCode::UCS_X_DIR
                    | HeaderVariableCode::UCS_Y_DIR => {
                        let mut v = Vector3::ZERO;
                        while matches!(reader.code(), 10 | 20 | 30) {
                            match reader.code() {
                                10 => v.x = reader.value().as_f64(),
                                20 => v.y = reader.value().as_f64(),
                                30 => v.z = reader.value().as_f64(),
                                _ => {}
                            }
                            if !reader.next()? {
                                break;
                            }
                        }
                        match var_name.as_str() {
                            HeaderVariableCode::INS_BASE => hv.ins_base = v,
                            HeaderVariableCode::UCS_ORG => hv.ucs_org = v,
                            HeaderVariableCode::UCS_X_DIR => hv.ucs_x_dir = v,
                            HeaderVariableCode::UCS_Y_DIR => hv.ucs_y_dir = v,
                            _ => {}
                        }
                        continue;
                    }
                    _ => {
                        // Store unrecognized header variable as custom variable
                        let code = reader.code();
                        if code == 10 {
                            let mut v = Vector3::new(reader.value().as_f64(), 0.0, 0.0);
                            let mut has_y = false;
                            let mut has_z = false;
                            while reader.next()? {
                                match reader.code() {
                                    20 => {
                                        v.y = reader.value().as_f64();
                                        has_y = true;
                                    }
                                    30 => {
                                        v.z = reader.value().as_f64();
                                        has_z = true;
                                    }
                                    _ => break,
                                }
                            }
                            let (group_code, val) = if has_z {
                                (30, HeaderVariableValue::Vector3(v))
                            } else if has_y {
                                (20, HeaderVariableValue::Vector3(v))
                            } else {
                                (10, HeaderVariableValue::Float(v.x))
                            };
                            hv.custom_variables.insert(
                                var_name.clone(),
                                HeaderVariable::new(var_name, group_code, val),
                            );
                            continue;
                        }
                        let val = match reader.value() {
                            crate::io::CodeValue::Double(d) => HeaderVariableValue::Float(*d),
                            crate::io::CodeValue::Int16(n) => HeaderVariableValue::Int16(*n),
                            crate::io::CodeValue::Int32(n) => HeaderVariableValue::Int32(*n),
                            crate::io::CodeValue::Bool(b) => HeaderVariableValue::Bool(*b),
                            other => HeaderVariableValue::String(other.to_string_value()),
                        };
                        hv.custom_variables
                            .insert(var_name.clone(), HeaderVariable::new(var_name, code, val));
                    }
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_classes(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !Self::at_section_end(reader) {
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::CLASS)
            {
                let mut cls = DxfClass::new("", "", "");
                while reader.next()? {
                    if reader.code() == 0 {
                        break;
                    }
                    match reader.code() {
                        1 => cls.dxf_name = reader.value().as_str().to_string(),
                        2 => cls.cpp_class_name = reader.value().as_str().to_string(),
                        3 => cls.application_name = reader.value().as_str().to_string(),
                        90 => cls.proxy_capabilities = reader.value().as_i32(),
                        91 => cls.instance_count = reader.value().as_i32(),
                        280 => cls.was_a_proxy = reader.value().as_i16() != 0,
                        281 => cls.is_an_entity = reader.value().as_i16() != 0,
                        _ => {}
                    }
                }
                if !cls.dxf_name.is_empty() {
                    doc.classes.push(cls);
                }
                continue;
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_tables(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !Self::at_section_end(reader) {
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::TABLE)
            {
                if !reader.next()? {
                    break;
                }
                let table_name = if reader.code() == 2 {
                    reader.value().as_str().to_ascii_uppercase()
                } else {
                    String::new()
                };
                match table_name.as_str() {
                    DxfObjectCode::LAYER_TABLE => Self::read_layers_table(reader, doc)?,
                    DxfObjectCode::LINETYPE_TABLE => Self::read_linetypes_table(reader, doc)?,
                    DxfObjectCode::TEXT_STYLE_TABLE => Self::read_styles_table(reader, doc)?,
                    DxfObjectCode::DIMENSION_STYLE_TABLE => Self::read_dimstyles_table(reader, doc)?,
                    DxfObjectCode::APPLICATION_ID_TABLE => Self::read_appids_table(reader, doc)?,
                    DxfObjectCode::UCS_TABLE => Self::read_ucs_table(reader, doc)?,
                    DxfObjectCode::VIEW_TABLE => Self::read_views_table(reader, doc)?,
                    DxfObjectCode::VPORT_TABLE => Self::read_vports_table(reader, doc)?,
                    _ => Self::skip_table(reader)?,
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn skip_table(reader: &mut dyn ICodeValueReader) -> Result<()> {
        while reader.next()? {
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::END_TABLE)
            {
                return Ok(());
            }
        }
        Ok(())
    }

    fn read_appids_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        while reader.next()? {
            if reader.code() == 0 {
                let s = reader.value().as_str();
                if s.eq_ignore_ascii_case(DxfObjectCode::END_TABLE) {
                    return Ok(());
                }
                if s.eq_ignore_ascii_case(DxfObjectCode::APP_ID) {
                    let mut name = String::new();
                    let mut handle = None;
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => handle = Some(reader.value().as_str().to_string()),
                            _ => {}
                        }
                    }
                    if !name.is_empty() {
                        if let Ok(mut app) = ApplicationRegistry::new(&name) {
                            app.table.object.handle = handle;
                            doc.application_registries.insert(app);
                        }
                    }
                    if reader.code() == 0
                        && reader
                            .value()
                            .as_str()
                            .eq_ignore_ascii_case(DxfObjectCode::END_TABLE)
                    {
                        return Ok(());
                    }
                }
            }
        }
        Ok(())
    }

    fn read_layers_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::LAYER {
                    let mut name = String::new();
                    let mut handle = None;
                    let mut color = AciColor::DEFAULT;
                    let mut is_visible = true;
                    let mut is_frozen = false;
                    let mut is_locked = false;
                    let mut plot = true;
                    let mut linetype = Linetype::CONTINUOUS_NAME.to_string();
                    let mut lineweight = Lineweight::Default;
                    let mut transparency = Transparency::from_cad_index(0);
                    let mut xdata = indexmap::IndexMap::new();

                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => handle = Some(reader.value().as_str().to_string()),
                            6 => linetype = reader.value().as_str().to_string(),
                            62 => {
                                let idx = reader.value().as_i16();
                                is_visible = idx >= 0;
                                color = AciColor::from_cad_index(idx.abs());
                            }
                            420 => color = AciColor::from_true_color(reader.value().as_i32()),
                            70 => {
                                let flags = LayerFlags::from_bits_truncate(reader.value().as_i16());
                                is_frozen = flags.contains(LayerFlags::FROZEN);
                                is_locked = flags.contains(LayerFlags::LOCKED);
                            }
                            290 => plot = reader.value().as_bool(),
                            370 => {
                                if let Some(lw) = Lineweight::from_i16(reader.value().as_i16()) {
                                    lineweight = lw;
                                }
                            }
                            440 => {
                                transparency =
                                    Transparency::from_alpha_value(reader.value().as_i32());
                            }
                            1001 => {
                                let xd = Self::read_xdata(reader)?;
                                xdata.insert(xd.application_registry.to_uppercase(), xd);
                                if reader.code() == 0 {
                                    break;
                                }
                                continue;
                            }
                            _ => {}
                        }
                    }

                    if !name.is_empty() {
                        let mut layer = if name.eq_ignore_ascii_case(Layer::DEFAULT_NAME) {
                            Layer::default_layer()
                        } else {
                            Layer::new(&name).unwrap_or_else(|_| {
                                let mut l = Layer::default_layer();
                                l.table.name = name.clone();
                                l.table.is_reserved = false;
                                l
                            })
                        };
                        layer.table.object.handle = handle;
                        layer.table.object.x_data = xdata;
                        layer.color = color;
                        layer.is_visible = is_visible;
                        layer.is_frozen = is_frozen;
                        layer.is_locked = is_locked;
                        layer.plot = plot;
                        layer.linetype = linetype;
                        layer.lineweight = lineweight;
                        layer.transparency = transparency;
                        doc.layers.insert(layer);
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_linetypes_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::LINETYPE {
                    let mut name = String::new();
                    let mut handle = None;
                    let mut description = String::new();
                    let mut segments = Vec::new();
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => handle = Some(reader.value().as_str().to_string()),
                            3 => description = reader.value().as_str().to_string(),
                            49 => segments.push(LinetypeSegment::Simple {
                                length: reader.value().as_f64(),
                            }),
                            _ => {}
                        }
                    }
                    if !name.is_empty() {
                        let mut lt = Linetype {
                            table: TableObjectData::new(&name, DxfObjectCode::LINETYPE, false)
                                .unwrap(),
                            description,
                            segments,
                        };
                        lt.table.is_reserved = name.eq_ignore_ascii_case(Linetype::BY_LAYER_NAME)
                            || name.eq_ignore_ascii_case(Linetype::BY_BLOCK_NAME)
                            || name.eq_ignore_ascii_case(Linetype::CONTINUOUS_NAME);
                        lt.table.object.handle = handle;
                        doc.linetypes.insert(lt);
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_styles_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::TEXT_STYLE {
                    let mut name = String::new();
                    let mut handle = None;
                    let mut flags = TextStyleFlags::NONE;
                    let mut height = 0.0;
                    let mut width_factor = 1.0;
                    let mut oblique_angle = 0.0;
                    let mut gen_flags = 0i16;
                    let mut font_file = TextStyle::DEFAULT_FONT.to_string();
                    let mut big_font = String::new();

                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => handle = Some(reader.value().as_str().to_string()),
                            70 => flags = TextStyleFlags::from_bits_truncate(reader.value().as_i16()),
                            40 => height = reader.value().as_f64(),
                            41 => width_factor = reader.value().as_f64(),
                            50 => oblique_angle = reader.value().as_f64(),
                            71 => gen_flags = reader.value().as_i16(),
                            3 => font_file = reader.value().as_str().to_string(),
                            4 => big_font = reader.value().as_str().to_string(),
                            _ => {}
                        }
                    }

                    if flags.contains(TextStyleFlags::SHAPE) {
                        if let Ok(mut ss) = ShapeStyle::new(&name, &font_file) {
                            ss.table.object.handle = handle;
                            ss.size = height;
                            ss.width_factor = width_factor;
                            ss.oblique_angle = oblique_angle;
                            doc.shape_styles.insert(ss);
                        }
                    } else if !name.is_empty() {
                        let mut ts = TextStyle {
                            table: TableObjectData::new(&name, DxfObjectCode::TEXT_STYLE, false)
                                .unwrap(),
                            font_file,
                            big_font,
                            font_family_name: String::new(),
                            font_style: FontStyle::REGULAR,
                            height,
                            is_backward: (gen_flags & 2) != 0,
                            is_upside_down: (gen_flags & 4) != 0,
                            is_vertical: flags.contains(TextStyleFlags::VERTICAL),
                            oblique_angle,
                            width_factor,
                        };
                        ts.table.is_reserved = name.eq_ignore_ascii_case(TextStyle::DEFAULT_NAME);
                        ts.table.object.handle = handle;
                        doc.text_styles.insert(ts);
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_dimstyles_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::DIM_STYLE {
                    let mut ds = DimensionStyle::default_style();
                    ds.table.is_reserved = false;
                    let mut name = String::new();
                    reader.set_code5_is_string(true);
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            105 => ds.table.object.handle = Some(reader.value().as_str().to_string()),
                            40 => ds.dim_scale_overall = reader.value().as_f64(),
                            41 => ds.arrow_size = reader.value().as_f64(),
                            42 => ds.ext_line_offset = reader.value().as_f64(),
                            43 => ds.dim_baseline_spacing = reader.value().as_f64(),
                            44 => ds.ext_line_extend = reader.value().as_f64(),
                            45 => ds.dim_roundoff = reader.value().as_f64(),
                            46 => ds.dim_line_extend = reader.value().as_f64(),
                            140 => ds.text_height = reader.value().as_f64(),
                            141 => ds.center_mark_size = reader.value().as_f64(),
                            144 => ds.dim_scale_linear = reader.value().as_f64(),
                            147 => ds.text_offset = reader.value().as_f64(),
                            176 => ds.dim_line_color = AciColor::from_cad_index(reader.value().as_i16()),
                            177 => ds.ext_line_color = AciColor::from_cad_index(reader.value().as_i16()),
                            178 => ds.text_color = AciColor::from_cad_index(reader.value().as_i16()),
                            271 => ds.length_precision = reader.value().as_i16(),
                            179 => ds.angular_precision = reader.value().as_i16(),
                            _ => {}
                        }
                    }
                    reader.set_code5_is_string(false);
                    if !name.is_empty() {
                        ds.table.name = name.clone();
                        ds.table.is_reserved = name.eq_ignore_ascii_case(DimensionStyle::DEFAULT_NAME);
                        doc.dimension_styles.insert(ds);
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_ucs_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::UCS {
                    let mut name = String::new();
                    let mut handle = None;
                    let mut origin = Vector3::ZERO;
                    let mut x_axis = Vector3::UNIT_X;
                    let mut y_axis = Vector3::UNIT_Y;
                    let mut elevation = 0.0;
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => handle = Some(reader.value().as_str().to_string()),
                            10 => origin.x = reader.value().as_f64(),
                            20 => origin.y = reader.value().as_f64(),
                            30 => origin.z = reader.value().as_f64(),
                            11 => x_axis.x = reader.value().as_f64(),
                            21 => x_axis.y = reader.value().as_f64(),
                            31 => x_axis.z = reader.value().as_f64(),
                            12 => y_axis.x = reader.value().as_f64(),
                            22 => y_axis.y = reader.value().as_f64(),
                            32 => y_axis.z = reader.value().as_f64(),
                            146 => elevation = reader.value().as_f64(),
                            _ => {}
                        }
                    }
                    if !name.is_empty() {
                        if let Ok(mut ucs) = UCS::from_origin_axes(&name, origin, x_axis, y_axis) {
                            ucs.table.object.handle = handle;
                            ucs.elevation = elevation;
                            doc.ucss.insert(ucs);
                        }
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_views_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::VIEW {
                    let mut name = String::new();
                    let mut handle = None;
                    let mut target = Vector3::ZERO;
                    let mut camera = Vector3::UNIT_Z;
                    let mut height = 1.0;
                    let mut width = 1.0;
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => handle = Some(reader.value().as_str().to_string()),
                            40 => height = reader.value().as_f64(),
                            41 => width = reader.value().as_f64(),
                            11 => camera.x = reader.value().as_f64(),
                            21 => camera.y = reader.value().as_f64(),
                            31 => camera.z = reader.value().as_f64(),
                            12 => target.x = reader.value().as_f64(),
                            22 => target.y = reader.value().as_f64(),
                            32 => target.z = reader.value().as_f64(),
                            _ => {}
                        }
                    }
                    if !name.is_empty() {
                        if let Ok(mut view) = View::new(&name) {
                            view.table.object.handle = handle;
                            view.target = target;
                            view.camera = camera;
                            view.height = height;
                            view.width = width;
                            doc.views.insert(view);
                        }
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_vports_table(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !reader.is_eof() {
            if reader.code() == 0 {
                let s = reader.value().as_str().to_ascii_uppercase();
                if s == DxfObjectCode::END_TABLE {
                    return Ok(());
                }
                if s == DxfObjectCode::VPORT {
                    let mut vp = VPort::active();
                    vp.table.is_reserved = false;
                    let mut name = String::new();
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        match reader.code() {
                            2 => name = reader.value().as_str().to_string(),
                            5 => vp.table.object.handle = Some(reader.value().as_str().to_string()),
                            12 => vp.view_center.x = reader.value().as_f64(),
                            22 => vp.view_center.y = reader.value().as_f64(),
                            13 => vp.snap_base_point.x = reader.value().as_f64(),
                            23 => vp.snap_base_point.y = reader.value().as_f64(),
                            14 => vp.snap_spacing.x = reader.value().as_f64(),
                            24 => vp.snap_spacing.y = reader.value().as_f64(),
                            15 => vp.grid_spacing.x = reader.value().as_f64(),
                            25 => vp.grid_spacing.y = reader.value().as_f64(),
                            16 => vp.direction.x = reader.value().as_f64(),
                            26 => vp.direction.y = reader.value().as_f64(),
                            36 => vp.direction.z = reader.value().as_f64(),
                            17 => vp.target.x = reader.value().as_f64(),
                            27 => vp.target.y = reader.value().as_f64(),
                            37 => vp.target.z = reader.value().as_f64(),
                            40 => vp.view_height = reader.value().as_f64(),
                            41 => vp.view_aspect_ratio = reader.value().as_f64(),
                            75 => vp.snap_mode = reader.value().as_i16() != 0,
                            76 => vp.show_grid = reader.value().as_i16() != 0,
                            _ => {}
                        }
                    }
                    if !name.is_empty() {
                        vp.table.name = name.clone();
                        vp.table.is_reserved = name.eq_ignore_ascii_case(VPort::DEFAULT_NAME);
                        doc.vports.insert(vp);
                    }
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_blocks(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !Self::at_section_end(reader) {
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::BEGIN_BLOCK)
            {
                let mut name = String::new();
                let mut handle = None;
                let mut layer = Layer::DEFAULT_NAME.to_string();
                let mut origin = Vector3::ZERO;
                let mut flags = BlockTypeFlags::NONE;
                let mut description = String::new();
                let mut xref_file = String::new();

                while reader.next()? {
                    if reader.code() == 0 {
                        break;
                    }
                    match reader.code() {
                        2 => name = reader.value().as_str().to_string(),
                        5 => handle = Some(reader.value().as_str().to_string()),
                        8 => layer = reader.value().as_str().to_string(),
                        10 => origin.x = reader.value().as_f64(),
                        20 => origin.y = reader.value().as_f64(),
                        30 => origin.z = reader.value().as_f64(),
                        70 => flags = BlockTypeFlags::from_bits_truncate(reader.value().as_i16()),
                        1 => xref_file = reader.value().as_str().to_string(),
                        4 => description = reader.value().as_str().to_string(),
                        _ => {}
                    }
                }

                let reserved = name.eq_ignore_ascii_case(Block::MODEL_SPACE_NAME)
                    || name.eq_ignore_ascii_case(Block::PAPER_SPACE_NAME);
                let mut block = Block::new_unchecked(&name, reserved);
                block.table.object.handle = handle;
                block.layer = layer;
                block.origin = origin;
                block.flags = flags;
                block.description = description;
                block.xref_file = xref_file;

                // Read entities inside the block until ENDBLK
                while !(reader.is_eof()
                    || (reader.code() == 0
                        && (reader
                            .value()
                            .as_str()
                            .eq_ignore_ascii_case(DxfObjectCode::END_BLOCK)
                            || reader
                                .value()
                                .as_str()
                                .eq_ignore_ascii_case(DxfObjectCode::END_SECTION))))
                {
                    if reader.code() == 0 {
                        let code_str = reader.value().as_str().to_ascii_uppercase();
                        if code_str == DxfObjectCode::ATTRIBUTE_DEFINITION {
                            let attdef = Self::read_attdef(reader)?;
                            block.add_attribute_definition(attdef);
                            continue;
                        }
                        if let Some(ent) = Self::read_entity(reader)? {
                            block.entities.push(ent);
                            continue;
                        }
                    }
                    if !reader.next()? {
                        break;
                    }
                }

                // Consume ENDBLK properties
                if reader.code() == 0
                    && reader
                        .value()
                        .as_str()
                        .eq_ignore_ascii_case(DxfObjectCode::END_BLOCK)
                {
                    while reader.next()? {
                        if reader.code() == 0 {
                            break;
                        }
                        if reader.code() == 5 {
                            block.end.object.handle = Some(reader.value().as_str().to_string());
                        }
                    }
                }

                if !name.is_empty() {
                    doc.blocks.insert(block);
                }
                continue;
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_entities(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !Self::at_section_end(reader) {
            if reader.code() == 0 {
                if let Some(ent) = Self::read_entity(reader)? {
                    doc.entities.add(ent);
                    continue;
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_objects(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        if !reader.next()? {
            return Ok(());
        }
        while !Self::at_section_end(reader) {
            if reader.code() == 0 {
                let obj_type = reader.value().as_str().to_ascii_uppercase();
                match obj_type.as_str() {
                    DxfObjectCode::GROUP => {
                        let mut handle = None;
                        let mut desc = String::new();
                        let mut is_unnamed = false;
                        let mut is_selectable = true;
                        let mut handles = Vec::new();
                        while reader.next()? {
                            if reader.code() == 0 {
                                break;
                            }
                            match reader.code() {
                                5 => handle = Some(reader.value().as_str().to_string()),
                                300 => desc = reader.value().as_str().to_string(),
                                70 => is_unnamed = reader.value().as_i16() != 0,
                                71 => is_selectable = reader.value().as_i16() != 0,
                                340 => handles.push(reader.value().as_str().to_string()),
                                _ => {}
                            }
                        }
                        let gname = handle.clone().unwrap_or_else(|| "*A1".to_string());
                        let mut grp = Group {
                            table: TableObjectData::new(&gname, DxfObjectCode::GROUP, false).unwrap(),
                            description: desc,
                            is_selectable,
                            is_unnamed,
                            entity_handles: handles,
                        };
                        grp.table.object.handle = handle;
                        doc.groups.insert(grp);
                        continue;
                    }
                    DxfObjectCode::MLINE_STYLE => {
                        let mut name = String::new();
                        let mut handle = None;
                        let mut desc = String::new();
                        let mut flags = MLineStyleFlags::NONE;
                        let mut fill_color = AciColor::BY_LAYER;
                        let mut start_angle = 90.0;
                        let mut end_angle = 90.0;
                        let mut elements = Vec::new();
                        let mut cur_elem: Option<MLineStyleElement> = None;
                        while reader.next()? {
                            if reader.code() == 0 {
                                break;
                            }
                            match reader.code() {
                                2 => name = reader.value().as_str().to_string(),
                                5 => handle = Some(reader.value().as_str().to_string()),
                                3 => desc = reader.value().as_str().to_string(),
                                70 => flags = MLineStyleFlags::from_bits_truncate(reader.value().as_i16()),
                                62 => fill_color = AciColor::from_cad_index(reader.value().as_i16()),
                                51 => start_angle = reader.value().as_f64(),
                                52 => end_angle = reader.value().as_f64(),
                                49 => {
                                    if let Some(el) = cur_elem.take() {
                                        elements.push(el);
                                    }
                                    cur_elem = Some(MLineStyleElement::new(reader.value().as_f64()));
                                }
                                6 => {
                                    if let Some(el) = &mut cur_elem {
                                        el.linetype = reader.value().as_str().to_string();
                                    }
                                }
                                _ => {}
                            }
                        }
                        if let Some(el) = cur_elem {
                            elements.push(el);
                        }
                        if !name.is_empty() {
                            let mut ms = MLineStyle {
                                table: TableObjectData::new(&name, DxfObjectCode::MLINE_STYLE, false)
                                    .unwrap(),
                                flags,
                                description: desc,
                                fill_color,
                                start_angle,
                                end_angle,
                                elements,
                            };
                            ms.table.is_reserved = name.eq_ignore_ascii_case(MLineStyle::DEFAULT_NAME);
                            ms.table.object.handle = handle;
                            doc.mline_styles.insert(ms);
                        }
                        continue;
                    }
                    DxfObjectCode::RASTER_VARIABLES => {
                        while reader.next()? {
                            if reader.code() == 0 {
                                break;
                            }
                            match reader.code() {
                                5 => doc.raster_variables.object.handle = Some(reader.value().as_str().to_string()),
                                70 => doc.raster_variables.display_frame = reader.value().as_i16() != 0,
                                71 => {
                                    doc.raster_variables.display_quality = if reader.value().as_i16() == 0 {
                                        ImageDisplayQuality::Draft
                                    } else {
                                        ImageDisplayQuality::High
                                    };
                                }
                                72 => {
                                    if let Some(u) = ImageUnits::from_i16(reader.value().as_i16()) {
                                        doc.raster_variables.units = u;
                                    }
                                }
                                _ => {}
                            }
                        }
                        continue;
                    }
                    "GEODATA" => {
                        let mut geo = DxfGeoData::default();
                        while reader.next()? {
                            if reader.code() == 0 { break; }
                            match reader.code() {
                                5 => geo.object.handle = Some(reader.value().as_str().to_string()),
                                90 => geo.version = reader.value().as_i32(),
                                330 => geo.host_block_handle = Some(reader.value().as_str().to_string()),
                                70 => geo.design_coordinates_type = reader.value().as_i16(),
                                10 => geo.design_point.x = reader.value().as_f64(),
                                20 => geo.design_point.y = reader.value().as_f64(),
                                30 => geo.design_point.z = reader.value().as_f64(),
                                11 => geo.reference_point.x = reader.value().as_f64(),
                                21 => geo.reference_point.y = reader.value().as_f64(),
                                31 => geo.reference_point.z = reader.value().as_f64(),
                                40 => geo.horizontal_unit_scale = reader.value().as_f64(),
                                91 => geo.horizontal_units = reader.value().as_i32(),
                                41 => geo.vertical_unit_scale = reader.value().as_f64(),
                                92 => geo.vertical_units = reader.value().as_i32(),
                                210 => geo.up_direction.x = reader.value().as_f64(),
                                220 => geo.up_direction.y = reader.value().as_f64(),
                                230 => geo.up_direction.z = reader.value().as_f64(),
                                12 => geo.north_direction.x = reader.value().as_f64(),
                                22 => geo.north_direction.y = reader.value().as_f64(),
                                95 => geo.scale_estimation_method = reader.value().as_i32(),
                                141 => geo.user_scale_factor = reader.value().as_f64(),
                                301 | 303 => geo.coordinate_system_definition.push_str(reader.value().as_str()),
                                302 => geo.geo_rss_tag = reader.value().as_str().to_string(),
                                305 => geo.observation_from_tag = reader.value().as_str().to_string(),
                                306 => geo.observation_to_tag = reader.value().as_str().to_string(),
                                307 => geo.observation_coverage_tag = reader.value().as_str().to_string(),
                                _ => {}
                            }
                        }
                        doc.geo_data = Some(geo);
                        continue;
                    }
                    "SUN" => {
                        let mut sun = DxfSun::default();
                        while reader.next()? {
                            if reader.code() == 0 { break; }
                            match reader.code() {
                                5 => sun.object.handle = Some(reader.value().as_str().to_string()),
                                90 => sun.version = reader.value().as_i32(),
                                290 => sun.status = reader.value().as_bool(),
                                63 => sun.color = AciColor::from_cad_index(reader.value().as_i16()),
                                40 => sun.intensity = reader.value().as_f64(),
                                291 => sun.shadows = reader.value().as_bool(),
                                91 => sun.julian_day = reader.value().as_i32(),
                                92 => sun.time_milliseconds = reader.value().as_i32(),
                                292 => sun.daylight_savings = reader.value().as_bool(),
                                70 => sun.shadow_type = reader.value().as_i16() as i32,
                                71 => sun.shadow_map_size = reader.value().as_i16(),
                                280 => sun.shadow_softness = reader.value().as_i16(),
                                _ => {}
                            }
                        }
                        doc.sun = Some(sun);
                        continue;
                    }
                    "SUNSTUDY" => {
                        let mut ss = DxfStoredSunStudy::default();
                        let mut pending_jd: Option<i32> = None;
                        while reader.next()? {
                            if reader.code() == 0 { break; }
                            match reader.code() {
                                5 => ss.object.handle = Some(reader.value().as_str().to_string()),
                                90 => ss.version = reader.value().as_i32(),
                                1 => ss.name = reader.value().as_str().to_string(),
                                2 => ss.description = reader.value().as_str().to_string(),
                                70 => ss.output_type = reader.value().as_i16(),
                                3 => ss.sheet_set_name = reader.value().as_str().to_string(),
                                290 => ss.use_subset = reader.value().as_bool(),
                                4 => ss.sheet_subset_name = reader.value().as_str().to_string(),
                                291 => ss.select_dates_from_calendar = reader.value().as_bool(),
                                91 => {
                                    if let Some(day) = pending_jd.take() {
                                        ss.julian_dates.push((day, reader.value().as_i32()));
                                    } else {
                                        pending_jd = Some(reader.value().as_i32());
                                    }
                                }
                                93 => ss.start_time_seconds = reader.value().as_i32(),
                                94 => ss.end_time_seconds = reader.value().as_i32(),
                                95 => ss.interval_seconds = reader.value().as_i32(),
                                293 => ss.lock_view_viewport = reader.value().as_bool(),
                                74 => ss.shade_plot_type = reader.value().as_i16(),
                                75 => ss.viewports_per_page = reader.value().as_i16() as i32,
                                40 => ss.spacing_between_viewports = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        doc.sun_studies.push(ss);
                        continue;
                    }
                    "MLEADERSTYLE" => {
                        let mut style = DxfMLeaderStyle::default_style();
                        style.table.is_reserved = false;
                        let mut name = String::new();
                        while reader.next()? {
                            if reader.code() == 0 { break; }
                            match reader.code() {
                                5 => style.table.object.handle = Some(reader.value().as_str().to_string()),
                                300 => name = reader.value().as_str().to_string(),
                                170 => {
                                    if let Some(ct) = MLeaderContentType::from_i16(reader.value().as_i16()) {
                                        style.content_type = ct;
                                    }
                                }
                                173 => style.leader_line_type = reader.value().as_i16(),
                                290 => style.enable_landing = reader.value().as_bool(),
                                42 => style.landing_gap = reader.value().as_f64(),
                                291 => style.enable_dogleg = reader.value().as_bool(),
                                43 => style.dogleg_length = reader.value().as_f64(),
                                44 => style.arrow_size = reader.value().as_f64(),
                                45 => style.text_height = reader.value().as_f64(),
                                142 => style.scale = reader.value().as_f64(),
                                _ => {}
                            }
                        }
                        if !name.is_empty() {
                            style.table.name = name.clone();
                            style.table.is_reserved = name.eq_ignore_ascii_case(DxfMLeaderStyle::DEFAULT_NAME);
                            doc.mleader_styles.insert(style);
                        }
                        continue;
                    }
                    "TABLESTYLE" => {
                        let mut style = DxfTableStyle::default_style();
                        style.table.is_reserved = false;
                        let mut name = String::new();
                        while reader.next()? {
                            if reader.code() == 0 { break; }
                            match reader.code() {
                                5 => style.table.object.handle = Some(reader.value().as_str().to_string()),
                                3 => name = reader.value().as_str().to_string(),
                                70 => style.flow_direction = reader.value().as_i16(),
                                40 => style.horizontal_cell_margin = reader.value().as_f64(),
                                41 => style.vertical_cell_margin = reader.value().as_f64(),
                                280 => style.suppress_title = reader.value().as_i16() != 0,
                                281 => style.suppress_header = reader.value().as_i16() != 0,
                                _ => {}
                            }
                        }
                        if !name.is_empty() {
                            style.table.name = name.clone();
                            style.table.is_reserved = name.eq_ignore_ascii_case(DxfTableStyle::DEFAULT_NAME);
                            doc.table_styles.insert(style);
                        }
                        continue;
                    }
                    _ => {
                        while reader.next()? {
                            if reader.code() == 0 {
                                break;
                            }
                        }
                        continue;
                    }
                }
            }
            if !reader.next()? {
                break;
            }
        }
        Ok(())
    }

    fn read_thumbnail(reader: &mut dyn ICodeValueReader, doc: &mut DxfDocument) -> Result<()> {
        while reader.next()? {
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::END_SECTION)
            {
                return Ok(());
            }
            if reader.code() == 310 {
                let bytes = reader.value().as_bytes();
                if !bytes.is_empty() {
                    doc.thumbnail_image.extend_from_slice(bytes);
                } else if let Some(decoded) = decode_hex(reader.value().as_str()) {
                    doc.thumbnail_image.extend_from_slice(&decoded);
                }
            }
        }
        Ok(())
    }

    fn apply_common_code(reader: &mut dyn ICodeValueReader, common: &mut EntityCommon) -> Result<bool> {
        match reader.code() {
            5 => common.object.handle = Some(reader.value().as_str().to_string()),
            330 => common.object.owner_handle = Some(reader.value().as_str().to_string()),
            8 => common.layer = reader.value().as_str().to_string(),
            6 => common.linetype = reader.value().as_str().to_string(),
            48 => common.linetype_scale = reader.value().as_f64(),
            60 => common.is_visible = reader.value().as_i16() == 0,
            62 => common.color = AciColor::from_cad_index(reader.value().as_i16()),
            420 => common.color = AciColor::from_true_color(reader.value().as_i32()),
            370 => {
                if let Some(lw) = Lineweight::from_i16(reader.value().as_i16()) {
                    common.lineweight = lw;
                }
            }
            440 => common.transparency = Transparency::from_alpha_value(reader.value().as_i32()),
            210 => common.normal.x = reader.value().as_f64(),
            220 => common.normal.y = reader.value().as_f64(),
            230 => common.normal.z = reader.value().as_f64(),
            1001 => {
                let xd = Self::read_xdata(reader)?;
                common.object.add_xdata(xd);
                return Ok(true);
            }
            _ => return Ok(false),
        }
        Ok(false)
    }

    fn read_xdata(reader: &mut dyn ICodeValueReader) -> Result<XData> {
        let app_reg = reader.value().as_str().to_string();
        let mut xdata = XData::new(app_reg);
        while reader.next()? {
            let code = reader.code();
            if !(1000..=1071).contains(&code) || code == 1001 {
                break;
            }
            if let Some(xcode) = XDataCode::from_i16(code) {
                let val = match xcode {
                    XDataCode::BinaryData => XDataValue::Binary(reader.value().as_bytes().to_vec()),
                    XDataCode::Int16 => XDataValue::Int16(reader.value().as_i16()),
                    XDataCode::Int32 => XDataValue::Int32(reader.value().as_i32()),
                    XDataCode::Real
                    | XDataCode::Distance
                    | XDataCode::ScaleFactor
                    | XDataCode::RealX
                    | XDataCode::RealY
                    | XDataCode::RealZ
                    | XDataCode::WorldSpacePositionX
                    | XDataCode::WorldSpacePositionY
                    | XDataCode::WorldSpacePositionZ
                    | XDataCode::WorldSpaceDisplacementX
                    | XDataCode::WorldSpaceDisplacementY
                    | XDataCode::WorldSpaceDisplacementZ
                    | XDataCode::WorldDirectionX
                    | XDataCode::WorldDirectionY
                    | XDataCode::WorldDirectionZ => XDataValue::Real(reader.value().as_f64()),
                    _ => XDataValue::String(reader.value().to_string_value()),
                };
                xdata.x_data_record.push(XDataRecord::new(xcode, val));
            }
        }
        Ok(xdata)
    }

    fn read_entity(reader: &mut dyn ICodeValueReader) -> Result<Option<EntityObject>> {
        let entity_code = reader.value().as_str().to_ascii_uppercase();
        let entity = match entity_code.as_str() {
            DxfObjectCode::POINT => Self::read_point(reader)?.into(),
            DxfObjectCode::LINE => Self::read_line(reader)?.into(),
            DxfObjectCode::RAY => Self::read_ray(reader)?.into(),
            DxfObjectCode::XLINE => Self::read_xline(reader)?.into(),
            DxfObjectCode::CIRCLE => Self::read_circle(reader)?.into(),
            DxfObjectCode::ARC => Self::read_arc(reader)?.into(),
            DxfObjectCode::ELLIPSE => Self::read_ellipse(reader)?.into(),
            DxfObjectCode::SOLID => Self::read_solid(reader)?.into(),
            DxfObjectCode::TRACE => Self::read_trace(reader)?.into(),
            DxfObjectCode::FACE_3D => Self::read_face3d(reader)?.into(),
            DxfObjectCode::TEXT => Self::read_text(reader)?.into(),
            DxfObjectCode::MTEXT => Self::read_mtext(reader)?.into(),
            DxfObjectCode::LW_POLYLINE => Self::read_lwpolyline(reader)?.into(),
            DxfObjectCode::POLYLINE => return Self::read_polyline(reader),
            DxfObjectCode::SPLINE => Self::read_spline(reader)?.into(),
            DxfObjectCode::HATCH => Self::read_hatch(reader)?.into(),
            DxfObjectCode::INSERT => Self::read_insert(reader)?.into(),
            DxfObjectCode::DIMENSION | DxfObjectCode::ARC_DIMENSION => {
                Self::read_dimension(reader)?.into()
            }
            DxfObjectCode::LEADER => Self::read_leader(reader)?.into(),
            DxfObjectCode::MLINE => Self::read_mline(reader)?.into(),
            DxfObjectCode::MESH => Self::read_mesh(reader)?.into(),
            DxfObjectCode::VIEWPORT => Self::read_viewport(reader)?.into(),
            DxfObjectCode::TOLERANCE => Self::read_tolerance(reader)?.into(),
            DxfObjectCode::SHAPE => Self::read_shape(reader)?.into(),
            DxfObjectCode::IMAGE => Self::read_image(reader)?.into(),
            DxfObjectCode::WIPEOUT => Self::read_wipeout(reader)?.into(),
            DxfObjectCode::UNDERLAY_PDF
            | DxfObjectCode::UNDERLAY_DWF
            | DxfObjectCode::UNDERLAY_DGN => Self::read_underlay(reader, &entity_code)?.into(),
            DxfObjectCode::SOLID_3D | DxfObjectCode::BODY | DxfObjectCode::REGION => {
                Self::read_acis_entity(reader, &entity_code)?
            }
            DxfObjectCode::ACAD_TABLE => Self::read_stored_table(reader)?.into(),
            DxfObjectCode::MULTILEADER => Self::read_multileader(reader)?.into(),
            DxfObjectCode::LIGHT => Self::read_light(reader)?.into(),
            DxfObjectCode::OLE2_FRAME => Self::read_ole2_frame(reader)?.into(),
            DxfObjectCode::OLE_FRAME => Self::read_ole_frame(reader)?.into(),
            DxfObjectCode::BEGIN_SECTION => Self::read_section_entity(reader)?.into(),
            DxfObjectCode::END_SEQUENCE | DxfObjectCode::VERTEX | DxfObjectCode::ATTRIBUTE => {
                while reader.next()? {
                    if reader.code() == 0 {
                        break;
                    }
                }
                return Ok(None);
            }
            _ => Self::read_opaque(reader, &entity_code)?.into(),
        };
        Ok(Some(entity))
    }

    fn read_point(reader: &mut dyn ICodeValueReader) -> Result<Point> {
        let mut p = Point::new(Vector3::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut p.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => p.position.x = reader.value().as_f64(),
                20 => p.position.y = reader.value().as_f64(),
                30 => p.position.z = reader.value().as_f64(),
                39 => p.thickness = reader.value().as_f64(),
                50 => p.rotation = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(p)
    }

    fn read_line(reader: &mut dyn ICodeValueReader) -> Result<Line> {
        let mut l = Line::new(Vector3::ZERO, Vector3::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut l.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => l.start_point.x = reader.value().as_f64(),
                20 => l.start_point.y = reader.value().as_f64(),
                30 => l.start_point.z = reader.value().as_f64(),
                11 => l.end_point.x = reader.value().as_f64(),
                21 => l.end_point.y = reader.value().as_f64(),
                31 => l.end_point.z = reader.value().as_f64(),
                39 => l.thickness = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(l)
    }

    fn read_ray(reader: &mut dyn ICodeValueReader) -> Result<Ray> {
        let mut r = Ray::new(Vector3::ZERO, Vector3::UNIT_X);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut r.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => r.origin.x = reader.value().as_f64(),
                20 => r.origin.y = reader.value().as_f64(),
                30 => r.origin.z = reader.value().as_f64(),
                11 => r.direction.x = reader.value().as_f64(),
                21 => r.direction.y = reader.value().as_f64(),
                31 => r.direction.z = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(r)
    }

    fn read_xline(reader: &mut dyn ICodeValueReader) -> Result<XLine> {
        let mut x = XLine::new(Vector3::ZERO, Vector3::UNIT_X);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut x.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => x.origin.x = reader.value().as_f64(),
                20 => x.origin.y = reader.value().as_f64(),
                30 => x.origin.z = reader.value().as_f64(),
                11 => x.direction.x = reader.value().as_f64(),
                21 => x.direction.y = reader.value().as_f64(),
                31 => x.direction.z = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(x)
    }

    fn read_circle(reader: &mut dyn ICodeValueReader) -> Result<Circle> {
        let mut c = Circle::new(Vector3::ZERO, 1.0);
        let mut ocs_center = Vector3::ZERO;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut c.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => ocs_center.x = reader.value().as_f64(),
                20 => ocs_center.y = reader.value().as_f64(),
                30 => ocs_center.z = reader.value().as_f64(),
                40 => c.radius = reader.value().as_f64().abs().max(f64::EPSILON),
                39 => c.thickness = reader.value().as_f64(),
                _ => {}
            }
        }
        c.center = MathHelper::transform_3d(
            ocs_center,
            c.common.normal,
            crate::coordinate_system::CoordinateSystem::Object,
            crate::coordinate_system::CoordinateSystem::World,
        );
        Ok(c)
    }

    fn read_arc(reader: &mut dyn ICodeValueReader) -> Result<Arc> {
        let mut a = Arc::new(Vector3::ZERO, 1.0, 0.0, 180.0);
        let mut ocs_center = Vector3::ZERO;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut a.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => ocs_center.x = reader.value().as_f64(),
                20 => ocs_center.y = reader.value().as_f64(),
                30 => ocs_center.z = reader.value().as_f64(),
                40 => a.radius = reader.value().as_f64().abs().max(f64::EPSILON),
                50 => a.start_angle = MathHelper::normalize_angle(reader.value().as_f64()),
                51 => a.end_angle = MathHelper::normalize_angle(reader.value().as_f64()),
                39 => a.thickness = reader.value().as_f64(),
                _ => {}
            }
        }
        a.center = MathHelper::transform_3d(
            ocs_center,
            a.common.normal,
            crate::coordinate_system::CoordinateSystem::Object,
            crate::coordinate_system::CoordinateSystem::World,
        );
        Ok(a)
    }

    fn read_ellipse(reader: &mut dyn ICodeValueReader) -> Result<Ellipse> {
        let mut e = Ellipse::new(Vector3::ZERO, 2.0, 1.0);
        let mut major_end = Vector3::UNIT_X;
        let mut ratio = 0.5;
        let mut start_param = 0.0;
        let mut end_param = MathHelper::TWO_PI;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut e.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => e.center.x = reader.value().as_f64(),
                20 => e.center.y = reader.value().as_f64(),
                30 => e.center.z = reader.value().as_f64(),
                11 => major_end.x = reader.value().as_f64(),
                21 => major_end.y = reader.value().as_f64(),
                31 => major_end.z = reader.value().as_f64(),
                40 => ratio = reader.value().as_f64(),
                41 => start_param = reader.value().as_f64(),
                42 => end_param = reader.value().as_f64(),
                _ => {}
            }
        }
        let half_major = major_end.modulus();
        e.major_axis = (half_major * 2.0).max(f64::EPSILON);
        e.minor_axis = (e.major_axis * ratio).abs().max(f64::EPSILON);
        let (maj_ocs, _) = MathHelper::transform_wcs_to_ocs(major_end, e.common.normal);
        e.rotation = MathHelper::normalize_angle(Vector2::angle(maj_ocs) * MathHelper::RAD_TO_DEG);
        if !MathHelper::is_equal(end_param - start_param, MathHelper::TWO_PI) {
            e.start_angle = MathHelper::normalize_angle(start_param * MathHelper::RAD_TO_DEG);
            e.end_angle = MathHelper::normalize_angle(end_param * MathHelper::RAD_TO_DEG);
        }
        Ok(e)
    }

    fn read_solid(reader: &mut dyn ICodeValueReader) -> Result<Solid> {
        let mut s = Solid::new(Vector2::ZERO, Vector2::ZERO, Vector2::ZERO, Vector2::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut s.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => s.first_vertex.x = reader.value().as_f64(),
                20 => s.first_vertex.y = reader.value().as_f64(),
                30 => s.elevation = reader.value().as_f64(),
                11 => s.second_vertex.x = reader.value().as_f64(),
                21 => s.second_vertex.y = reader.value().as_f64(),
                12 => s.third_vertex.x = reader.value().as_f64(),
                22 => s.third_vertex.y = reader.value().as_f64(),
                13 => s.fourth_vertex.x = reader.value().as_f64(),
                23 => s.fourth_vertex.y = reader.value().as_f64(),
                39 => s.thickness = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(s)
    }

    fn read_trace(reader: &mut dyn ICodeValueReader) -> Result<Trace> {
        let mut t = Trace::new(Vector2::ZERO, Vector2::ZERO, Vector2::ZERO, Vector2::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut t.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => t.first_vertex.x = reader.value().as_f64(),
                20 => t.first_vertex.y = reader.value().as_f64(),
                30 => t.elevation = reader.value().as_f64(),
                11 => t.second_vertex.x = reader.value().as_f64(),
                21 => t.second_vertex.y = reader.value().as_f64(),
                12 => t.third_vertex.x = reader.value().as_f64(),
                22 => t.third_vertex.y = reader.value().as_f64(),
                13 => t.fourth_vertex.x = reader.value().as_f64(),
                23 => t.fourth_vertex.y = reader.value().as_f64(),
                39 => t.thickness = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(t)
    }

    fn read_face3d(reader: &mut dyn ICodeValueReader) -> Result<Face3D> {
        let mut f = Face3D::new(Vector3::ZERO, Vector3::ZERO, Vector3::ZERO, Vector3::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut f.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => f.first_vertex.x = reader.value().as_f64(),
                20 => f.first_vertex.y = reader.value().as_f64(),
                30 => f.first_vertex.z = reader.value().as_f64(),
                11 => f.second_vertex.x = reader.value().as_f64(),
                21 => f.second_vertex.y = reader.value().as_f64(),
                31 => f.second_vertex.z = reader.value().as_f64(),
                12 => f.third_vertex.x = reader.value().as_f64(),
                22 => f.third_vertex.y = reader.value().as_f64(),
                32 => f.third_vertex.z = reader.value().as_f64(),
                13 => f.fourth_vertex.x = reader.value().as_f64(),
                23 => f.fourth_vertex.y = reader.value().as_f64(),
                33 => f.fourth_vertex.z = reader.value().as_f64(),
                70 => f.edge_flags = Face3DEdgeFlags::from_bits_truncate(reader.value().as_i16()),
                _ => {}
            }
        }
        Ok(f)
    }

    fn read_text(reader: &mut dyn ICodeValueReader) -> Result<Text> {
        let mut t = Text::new("", Vector3::ZERO, 1.0);
        let mut first_pt = Vector3::ZERO;
        let mut second_pt = Vector3::ZERO;
        let mut has_second = false;
        let mut h_align = 0i16;
        let mut v_align = 0i16;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut t.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                1 => t.value = reader.value().as_str().to_string(),
                7 => t.style = reader.value().as_str().to_string(),
                10 => first_pt.x = reader.value().as_f64(),
                20 => first_pt.y = reader.value().as_f64(),
                30 => first_pt.z = reader.value().as_f64(),
                11 => { second_pt.x = reader.value().as_f64(); has_second = true; }
                21 => second_pt.y = reader.value().as_f64(),
                31 => second_pt.z = reader.value().as_f64(),
                39 => t.thickness = reader.value().as_f64(),
                40 => t.height = reader.value().as_f64().abs().max(f64::EPSILON),
                41 => t.width_factor = reader.value().as_f64(),
                50 => t.rotation = reader.value().as_f64(),
                51 => t.oblique_angle = reader.value().as_f64(),
                71 => {
                    let g = reader.value().as_i16();
                    t.is_backward = (g & 2) != 0;
                    t.is_upside_down = (g & 4) != 0;
                }
                72 => h_align = reader.value().as_i16(),
                73 => v_align = reader.value().as_i16(),
                _ => {}
            }
        }
        t.alignment = TextAlignment::from_dxf_codes(h_align, v_align);
        let ocs_pos = if has_second && t.alignment != TextAlignment::BaselineLeft {
            second_pt
        } else {
            first_pt
        };
        t.position = MathHelper::transform_3d(
            ocs_pos,
            t.common.normal,
            crate::coordinate_system::CoordinateSystem::Object,
            crate::coordinate_system::CoordinateSystem::World,
        );
        Ok(t)
    }

    fn read_mtext(reader: &mut dyn ICodeValueReader) -> Result<MText> {
        let mut mt = MText::new("", Vector3::ZERO, 1.0);
        let mut text_buf = String::new();
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut mt.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                3 | 1 => text_buf.push_str(reader.value().as_str()),
                7 => mt.style = reader.value().as_str().to_string(),
                10 => mt.position.x = reader.value().as_f64(),
                20 => mt.position.y = reader.value().as_f64(),
                30 => mt.position.z = reader.value().as_f64(),
                40 => mt.height = reader.value().as_f64().abs().max(f64::EPSILON),
                41 => mt.rectangle_width = reader.value().as_f64(),
                44 => mt.line_spacing_factor = reader.value().as_f64(),
                50 => mt.rotation = reader.value().as_f64(),
                71 => {
                    if let Some(ap) = MTextAttachmentPoint::from_i16(reader.value().as_i16()) {
                        mt.attachment_point = ap;
                    }
                }
                72 => {
                    if let Some(dd) = MTextDrawingDirection::from_i16(reader.value().as_i16()) {
                        mt.drawing_direction = dd;
                    }
                }
                73 => {
                    if let Some(ls) = MTextLineSpacingStyle::from_i16(reader.value().as_i16()) {
                        mt.line_spacing_style = ls;
                    }
                }
                _ => {}
            }
        }
        mt.value = text_buf;
        Ok(mt)
    }

    fn read_lwpolyline(reader: &mut dyn ICodeValueReader) -> Result<Polyline2D> {
        let mut pl = Polyline2D::new(Vec::new(), false);
        let mut constant_width = 0.0;
        let mut current: Option<Polyline2DVertex> = None;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut pl.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => {
                    if let Some(v) = current.take() {
                        pl.vertexes.push(v);
                    }
                    let mut v = Polyline2DVertex::new(reader.value().as_f64(), 0.0);
                    v.start_width = constant_width;
                    v.end_width = constant_width;
                    current = Some(v);
                }
                20 => {
                    if let Some(v) = &mut current {
                        v.position.y = reader.value().as_f64();
                    }
                }
                40 => {
                    if let Some(v) = &mut current {
                        v.start_width = reader.value().as_f64();
                    }
                }
                41 => {
                    if let Some(v) = &mut current {
                        v.end_width = reader.value().as_f64();
                    }
                }
                42 => {
                    if let Some(v) = &mut current {
                        v.bulge = reader.value().as_f64();
                    }
                }
                43 => constant_width = reader.value().as_f64(),
                38 => pl.elevation = reader.value().as_f64(),
                39 => pl.thickness = reader.value().as_f64(),
                70 => {
                    let f = reader.value().as_i16();
                    pl.is_closed = (f & 1) != 0;
                    pl.linetype_generation = (f & 128) != 0;
                }
                _ => {}
            }
        }
        if let Some(v) = current {
            pl.vertexes.push(v);
        }
        Ok(pl)
    }

    fn read_polyline(reader: &mut dyn ICodeValueReader) -> Result<Option<EntityObject>> {
        let mut common = EntityCommon::new(EntityType::Polyline3D, DxfObjectCode::POLYLINE);
        let mut flags = PolylineTypeFlags::OPEN_POLYLINE;
        let mut smooth = PolylineSmoothType::NoSmooth;
        let mut m = 0i16;
        let mut n = 0i16;
        let mut density_m = 0i16;
        let mut density_n = 0i16;
        let mut elevation = 0.0;
        let mut thickness = 0.0;

        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                30 => elevation = reader.value().as_f64(),
                39 => thickness = reader.value().as_f64(),
                70 => flags = PolylineTypeFlags::from_bits_truncate(reader.value().as_i16()),
                71 => m = reader.value().as_i16(),
                72 => n = reader.value().as_i16(),
                73 => density_m = reader.value().as_i16(),
                74 => density_n = reader.value().as_i16(),
                75 => {
                    if let Some(s) = PolylineSmoothType::from_i16(reader.value().as_i16()) {
                        smooth = s;
                    }
                }
                _ => {}
            }
        }

        let mut verts3d = Vec::new();
        let mut verts2d = Vec::new();
        let mut faces = Vec::new();

        while !reader.is_eof()
            && reader.code() == 0
            && reader
                .value()
                .as_str()
                .eq_ignore_ascii_case(DxfObjectCode::VERTEX)
        {
            let mut pos = Vector3::ZERO;
            let mut bulge = 0.0;
            let mut sw = 0.0;
            let mut ew = 0.0;
            let mut vflags = 0i16;
            let mut fidx = [0i16; 4];
            while reader.next()? {
                if reader.code() == 0 { break; }
                match reader.code() {
                    10 => pos.x = reader.value().as_f64(),
                    20 => pos.y = reader.value().as_f64(),
                    30 => pos.z = reader.value().as_f64(),
                    40 => sw = reader.value().as_f64(),
                    41 => ew = reader.value().as_f64(),
                    42 => bulge = reader.value().as_f64(),
                    70 => vflags = reader.value().as_i16(),
                    71 => fidx[0] = reader.value().as_i16(),
                    72 => fidx[1] = reader.value().as_i16(),
                    73 => fidx[2] = reader.value().as_i16(),
                    74 => fidx[3] = reader.value().as_i16(),
                    _ => {}
                }
            }
            if flags.contains(PolylineTypeFlags::POLYFACE_MESH) && (vflags & 128) != 0 && (vflags & 64) == 0 {
                let idx_vec: Vec<i16> = fidx.into_iter().filter(|&x| x != 0).collect();
                if !idx_vec.is_empty() {
                    faces.push(PolyfaceMeshFace::new(idx_vec));
                }
            } else {
                verts3d.push(pos);
                verts2d.push(Polyline2DVertex {
                    position: pos.to_vector2(),
                    start_width: sw,
                    end_width: ew,
                    bulge,
                });
            }
        }

        // Consume SEQEND
        if reader.code() == 0
            && reader
                .value()
                .as_str()
                .eq_ignore_ascii_case(DxfObjectCode::END_SEQUENCE)
        {
            while reader.next()? {
                if reader.code() == 0 { break; }
            }
        }

        if flags.contains(PolylineTypeFlags::POLYFACE_MESH) {
            common.entity_type = EntityType::PolyfaceMesh;
            let mut pf = PolyfaceMesh::new(verts3d, faces);
            pf.common = common;
            Ok(Some(pf.into()))
        } else if flags.contains(PolylineTypeFlags::POLYGON_MESH_3D) {
            common.entity_type = EntityType::PolygonMesh;
            let mut pm = PolygonMesh::new(m, n, verts3d);
            pm.common = common;
            pm.is_closed_in_m = flags.contains(PolylineTypeFlags::CLOSED_POLYLINE_OR_CLOSED_POLYGON_MESH_IN_M);
            pm.is_closed_in_n = flags.contains(PolylineTypeFlags::CLOSED_POLYGON_MESH_IN_N);
            pm.smooth_type = smooth;
            pm.density_m = density_m;
            pm.density_n = density_n;
            Ok(Some(pm.into()))
        } else if flags.contains(PolylineTypeFlags::POLYLINE_3D) {
            let mut pl3 = Polyline3D::new(
                verts3d,
                flags.contains(PolylineTypeFlags::CLOSED_POLYLINE_OR_CLOSED_POLYGON_MESH_IN_M),
            );
            pl3.common = common;
            pl3.smooth_type = smooth;
            pl3.linetype_generation = flags.contains(PolylineTypeFlags::CONTINUOUS_LINETYPE_PATTERN);
            Ok(Some(pl3.into()))
        } else {
            common.entity_type = EntityType::Polyline2D;
            common.object.code_name = DxfObjectCode::LW_POLYLINE.to_string();
            let mut pl2 = Polyline2D::new(
                verts2d,
                flags.contains(PolylineTypeFlags::CLOSED_POLYLINE_OR_CLOSED_POLYGON_MESH_IN_M),
            );
            pl2.common = common;
            pl2.elevation = elevation;
            pl2.thickness = thickness;
            pl2.linetype_generation = flags.contains(PolylineTypeFlags::CONTINUOUS_LINETYPE_PATTERN);
            Ok(Some(pl2.into()))
        }
    }

    fn read_spline(reader: &mut dyn ICodeValueReader) -> Result<Spline> {
        let mut s = Spline::from_control_points(Vec::new(), 3, false);
        let mut cur_ctrl: Option<Vector3> = None;
        let mut cur_fit: Option<Vector3> = None;
        let mut start_t = Vector3::ZERO;
        let mut has_st = false;
        let mut end_t = Vector3::ZERO;
        let mut has_et = false;

        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut s.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                70 => s.flags = SplineTypeFlags::from_bits_truncate(reader.value().as_i16()),
                71 => s.degree = reader.value().as_i16(),
                40 => s.knots.push(reader.value().as_f64()),
                41 => s.weights.push(reader.value().as_f64()),
                42 => s.knot_tolerance = reader.value().as_f64(),
                43 => s.control_point_tolerance = reader.value().as_f64(),
                44 => s.fit_tolerance = reader.value().as_f64(),
                10 => {
                    if let Some(p) = cur_ctrl.take() { s.control_points.push(p); }
                    cur_ctrl = Some(Vector3::new(reader.value().as_f64(), 0.0, 0.0));
                }
                20 => { if let Some(p) = &mut cur_ctrl { p.y = reader.value().as_f64(); } }
                30 => { if let Some(p) = &mut cur_ctrl { p.z = reader.value().as_f64(); } }
                11 => {
                    if let Some(p) = cur_fit.take() { s.fit_points.push(p); }
                    cur_fit = Some(Vector3::new(reader.value().as_f64(), 0.0, 0.0));
                }
                21 => { if let Some(p) = &mut cur_fit { p.y = reader.value().as_f64(); } }
                31 => { if let Some(p) = &mut cur_fit { p.z = reader.value().as_f64(); } }
                12 => { start_t.x = reader.value().as_f64(); has_st = true; }
                22 => start_t.y = reader.value().as_f64(),
                32 => start_t.z = reader.value().as_f64(),
                13 => { end_t.x = reader.value().as_f64(); has_et = true; }
                23 => end_t.y = reader.value().as_f64(),
                33 => end_t.z = reader.value().as_f64(),
                _ => {}
            }
        }
        if let Some(p) = cur_ctrl { s.control_points.push(p); }
        if let Some(p) = cur_fit { s.fit_points.push(p); }
        if s.weights.is_empty() && !s.control_points.is_empty() {
            s.weights = vec![1.0; s.control_points.len()];
        }
        if has_st { s.start_tangent = Some(start_t); }
        if has_et { s.end_tangent = Some(end_t); }
        if s.control_points.is_empty() && !s.fit_points.is_empty() {
            s.creation_method = SplineCreationMethod::FitPoints;
        }
        Ok(s)
    }

    fn read_hatch(reader: &mut dyn ICodeValueReader) -> Result<Hatch> {
        let mut h = Hatch::new(HatchPattern::solid(), Vec::new());
        let mut num_paths = 0i32;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut h.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                2 => h.pattern.name = reader.value().as_str().to_string(),
                30 => h.elevation = reader.value().as_f64(),
                70 => {
                    h.pattern.fill = if reader.value().as_i16() == 1 {
                        HatchFillType::SolidFill
                    } else {
                        HatchFillType::PatternFill
                    };
                }
                71 => h.associative = reader.value().as_i16() != 0,
                91 => {
                    num_paths = reader.value().as_i32();
                    for _ in 0..num_paths {
                        if let Some(path) = Self::read_hatch_boundary_path(reader)? {
                            h.boundary_paths.push(path);
                        }
                        if reader.code() == 0 {
                            return Ok(h);
                        }
                    }
                }
                75 => {
                    h.pattern.style = match reader.value().as_i16() {
                        1 => HatchStyle::Outer,
                        2 => HatchStyle::Ignore,
                        _ => HatchStyle::Normal,
                    };
                }
                76 => {
                    h.pattern.hatch_type = match reader.value().as_i16() {
                        0 => HatchType::UserDefined,
                        2 => HatchType::Custom,
                        _ => HatchType::Predefined,
                    };
                }
                52 => h.pattern.angle = reader.value().as_f64(),
                41 => h.pattern.scale = reader.value().as_f64(),
                _ => {}
            }
        }
        let _ = num_paths;
        Ok(h)
    }

    fn read_hatch_boundary_path(reader: &mut dyn ICodeValueReader) -> Result<Option<HatchBoundaryPath>> {
        if !reader.next()? {
            return Ok(None);
        }
        if reader.code() != 92 {
            return Ok(None);
        }
        let flags = HatchBoundaryPathTypeFlags::from_bits_truncate(reader.value().as_i32());
        let mut edges = Vec::new();

        if flags.contains(HatchBoundaryPathTypeFlags::POLYLINE) {
            let mut has_bulge = false;
            let mut is_closed = false;
            let mut num_verts = 0i32;
            let mut verts = Vec::new();
            while reader.next()? {
                match reader.code() {
                    72 => has_bulge = reader.value().as_i16() != 0,
                    73 => is_closed = reader.value().as_i16() != 0,
                    93 => {
                        num_verts = reader.value().as_i32();
                        break;
                    }
                    0 => return Ok(None),
                    _ => {}
                }
            }
            for _ in 0..num_verts {
                let mut x = 0.0;
                let mut y = 0.0;
                let mut bulge = 0.0;
                if reader.next()? && reader.code() == 10 { x = reader.value().as_f64(); }
                if reader.next()? && reader.code() == 20 { y = reader.value().as_f64(); }
                if has_bulge && reader.next()? && reader.code() == 42 {
                    bulge = reader.value().as_f64();
                }
                verts.push((Vector2::new(x, y), bulge));
            }
            edges.push(HatchBoundaryPathEdge::Polyline { vertexes: verts, is_closed });
        } else {
            let mut num_edges = 0i32;
            if reader.next()? && reader.code() == 93 {
                num_edges = reader.value().as_i32();
            }
            for _ in 0..num_edges {
                if !reader.next()? { break; }
                let edge_type = if reader.code() == 72 { reader.value().as_i16() } else { 0 };
                match edge_type {
                    1 => {
                        // Line
                        let (mut sx, mut sy, mut ex, mut ey) = (0.0, 0.0, 0.0, 0.0);
                        if reader.next()? && reader.code() == 10 { sx = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 20 { sy = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 11 { ex = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 21 { ey = reader.value().as_f64(); }
                        edges.push(HatchBoundaryPathEdge::Line {
                            start: Vector2::new(sx, sy),
                            end: Vector2::new(ex, ey),
                        });
                    }
                    2 => {
                        // Arc
                        let (mut cx, mut cy, mut r, mut sa, mut ea, mut ccw) = (0.0, 0.0, 1.0, 0.0, 360.0, true);
                        if reader.next()? && reader.code() == 10 { cx = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 20 { cy = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 40 { r = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 50 { sa = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 51 { ea = reader.value().as_f64(); }
                        if reader.next()? && reader.code() == 73 { ccw = reader.value().as_i16() != 0; }
                        edges.push(HatchBoundaryPathEdge::Arc {
                            center: Vector2::new(cx, cy),
                            radius: r,
                            start_angle: sa,
                            end_angle: ea,
                            is_counter_clockwise: ccw,
                        });
                    }
                    _ => {}
                }
            }
        }

        // Skip source boundary object handles (code 97 + N * 330)
        if reader.next()? && reader.code() == 97 {
            let count = reader.value().as_i32();
            for _ in 0..count {
                let _ = reader.next()?;
            }
        }

        Ok(Some(HatchBoundaryPath {
            path_type_flags: flags,
            edges,
        }))
    }

    fn read_insert(reader: &mut dyn ICodeValueReader) -> Result<Insert> {
        let mut ins = Insert::new("");
        let mut has_attribs = false;
        let mut ocs_pos = Vector3::ZERO;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut ins.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                2 => ins.block_name = reader.value().as_str().to_string(),
                10 => ocs_pos.x = reader.value().as_f64(),
                20 => ocs_pos.y = reader.value().as_f64(),
                30 => ocs_pos.z = reader.value().as_f64(),
                41 => ins.scale.x = reader.value().as_f64(),
                42 => ins.scale.y = reader.value().as_f64(),
                43 => ins.scale.z = reader.value().as_f64(),
                50 => ins.rotation = reader.value().as_f64(),
                66 => has_attribs = reader.value().as_i16() != 0,
                70 => ins.column_count = reader.value().as_i16(),
                71 => ins.row_count = reader.value().as_i16(),
                44 => ins.column_spacing = reader.value().as_f64(),
                45 => ins.row_spacing = reader.value().as_f64(),
                _ => {}
            }
        }
        ins.position = MathHelper::transform_3d(
            ocs_pos,
            ins.common.normal,
            crate::coordinate_system::CoordinateSystem::Object,
            crate::coordinate_system::CoordinateSystem::World,
        );
        if has_attribs {
            while !reader.is_eof()
                && reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::ATTRIBUTE)
            {
                let mut att = Attribute::new("", "");
                while reader.next()? {
                    if reader.code() == 0 { break; }
                    match reader.code() {
                        5 => att.object.handle = Some(reader.value().as_str().to_string()),
                        1 => att.value = reader.value().as_str().to_string(),
                        2 => att.tag = reader.value().as_str().to_string(),
                        7 => att.style = reader.value().as_str().to_string(),
                        8 => att.layer = reader.value().as_str().to_string(),
                        10 => att.position.x = reader.value().as_f64(),
                        20 => att.position.y = reader.value().as_f64(),
                        30 => att.position.z = reader.value().as_f64(),
                        40 => att.height = reader.value().as_f64(),
                        50 => att.rotation = reader.value().as_f64(),
                        70 => att.flags = AttributeFlags::from_bits_truncate(reader.value().as_i16()),
                        _ => {}
                    }
                }
                ins.attributes.push(att);
            }
            if reader.code() == 0
                && reader
                    .value()
                    .as_str()
                    .eq_ignore_ascii_case(DxfObjectCode::END_SEQUENCE)
            {
                while reader.next()? {
                    if reader.code() == 0 { break; }
                }
            }
        }
        Ok(ins)
    }

    fn read_attdef(reader: &mut dyn ICodeValueReader) -> Result<AttributeDefinition> {
        let mut ad = AttributeDefinition::new("");
        while reader.next()? {
            if reader.code() == 0 { break; }
            match reader.code() {
                5 => ad.object.handle = Some(reader.value().as_str().to_string()),
                1 => ad.value = reader.value().as_str().to_string(),
                2 => ad.tag = reader.value().as_str().to_string(),
                3 => ad.prompt = reader.value().as_str().to_string(),
                7 => ad.style = reader.value().as_str().to_string(),
                8 => ad.layer = reader.value().as_str().to_string(),
                10 => ad.position.x = reader.value().as_f64(),
                20 => ad.position.y = reader.value().as_f64(),
                30 => ad.position.z = reader.value().as_f64(),
                40 => ad.height = reader.value().as_f64(),
                50 => ad.rotation = reader.value().as_f64(),
                70 => ad.flags = AttributeFlags::from_bits_truncate(reader.value().as_i16()),
                _ => {}
            }
        }
        Ok(ad)
    }

    fn read_dimension(reader: &mut dyn ICodeValueReader) -> Result<Dimension> {
        let mut d = Dimension::linear(Vector2::ZERO, Vector2::ZERO, 0.0, 0.0);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut d.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                1 => {
                    let s = reader.value().as_str();
                    if !s.is_empty() { d.user_text = Some(s.to_string()); }
                }
                2 => d.block_name = reader.value().as_str().to_string(),
                3 => d.style = reader.value().as_str().to_string(),
                10 => d.definition_point.x = reader.value().as_f64(),
                20 => d.definition_point.y = reader.value().as_f64(),
                30 => d.definition_point.z = reader.value().as_f64(),
                11 => d.text_reference_point.x = reader.value().as_f64(),
                21 => d.text_reference_point.y = reader.value().as_f64(),
                31 => d.elevation = reader.value().as_f64(),
                13 => d.first_reference_point.x = reader.value().as_f64(),
                23 => d.first_reference_point.y = reader.value().as_f64(),
                33 => d.first_reference_point.z = reader.value().as_f64(),
                14 => d.second_reference_point.x = reader.value().as_f64(),
                24 => d.second_reference_point.y = reader.value().as_f64(),
                34 => d.second_reference_point.z = reader.value().as_f64(),
                15 => d.arc_definition_point.x = reader.value().as_f64(),
                25 => d.arc_definition_point.y = reader.value().as_f64(),
                35 => d.arc_definition_point.z = reader.value().as_f64(),
                40 => d.leader_length = reader.value().as_f64(),
                41 => d.line_spacing_factor = reader.value().as_f64(),
                42 => d.measurement = reader.value().as_f64(),
                50 => d.rotation = reader.value().as_f64(),
                52 => d.oblique_angle = reader.value().as_f64(),
                53 => d.text_rotation = reader.value().as_f64(),
                70 => {
                    if let Some(dt) = DimensionType::from_i16(reader.value().as_i16()) {
                        d.dimension_type = dt;
                    }
                }
                71 => {
                    if let Some(ap) = MTextAttachmentPoint::from_i16(reader.value().as_i16()) {
                        d.attachment_point = ap;
                    }
                }
                72 => {
                    if let Some(ls) = MTextLineSpacingStyle::from_i16(reader.value().as_i16()) {
                        d.line_spacing_style = ls;
                    }
                }
                _ => {}
            }
        }
        Ok(d)
    }

    fn read_leader(reader: &mut dyn ICodeValueReader) -> Result<Leader> {
        let mut l = Leader::new(Vec::new());
        let mut cur: Option<Vector2> = None;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut l.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                3 => l.style = reader.value().as_str().to_string(),
                71 => l.show_arrowhead = reader.value().as_i16() != 0,
                72 => {
                    l.path_type = if reader.value().as_i16() == 1 {
                        LeaderPathType::Spline
                    } else {
                        LeaderPathType::StraightLineSegments
                    };
                }
                75 => l.has_hookline = reader.value().as_i16() != 0,
                10 => {
                    if let Some(v) = cur.take() { l.vertexes.push(v); }
                    cur = Some(Vector2::new(reader.value().as_f64(), 0.0));
                }
                20 => { if let Some(v) = &mut cur { v.y = reader.value().as_f64(); } }
                30 => l.elevation = reader.value().as_f64(),
                340 => l.annotation_handle = Some(reader.value().as_str().to_string()),
                _ => {}
            }
        }
        if let Some(v) = cur { l.vertexes.push(v); }
        Ok(l)
    }

    fn read_mline(reader: &mut dyn ICodeValueReader) -> Result<MLine> {
        let mut ml = MLine::new(Vec::new());
        let mut cur: Option<MLineVertex> = None;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut ml.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                2 => ml.style = reader.value().as_str().to_string(),
                40 => ml.scale = reader.value().as_f64(),
                70 => {
                    if let Some(j) = MLineJustification::from_i16(reader.value().as_i16()) {
                        ml.justification = j;
                    }
                }
                71 => ml.flags = MLineFlags::from_bits_truncate(reader.value().as_i16()),
                11 => {
                    if let Some(v) = cur.take() { ml.vertexes.push(v); }
                    cur = Some(MLineVertex::new(Vector2::new(reader.value().as_f64(), 0.0)));
                }
                21 => { if let Some(v) = &mut cur { v.position.y = reader.value().as_f64(); } }
                31 => ml.elevation = reader.value().as_f64(),
                12 => { if let Some(v) = &mut cur { v.direction.x = reader.value().as_f64(); } }
                22 => { if let Some(v) = &mut cur { v.direction.y = reader.value().as_f64(); } }
                13 => { if let Some(v) = &mut cur { v.miter.x = reader.value().as_f64(); } }
                23 => { if let Some(v) = &mut cur { v.miter.y = reader.value().as_f64(); } }
                _ => {}
            }
        }
        if let Some(v) = cur { ml.vertexes.push(v); }
        Ok(ml)
    }

    fn read_mesh(reader: &mut dyn ICodeValueReader) -> Result<Mesh> {
        let mut m = Mesh::new(Vec::new(), Vec::new());
        let mut cur_v: Option<Vector3> = None;
        let mut reading_faces = false;
        let mut face_remaining = 0usize;
        let mut cur_face = Vec::new();
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut m.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                72 => m.subdivision_level = reader.value().as_i16().max(0) as u8,
                10 => {
                    if let Some(v) = cur_v.take() { m.vertexes.push(v); }
                    cur_v = Some(Vector3::new(reader.value().as_f64(), 0.0, 0.0));
                }
                20 => { if let Some(v) = &mut cur_v { v.y = reader.value().as_f64(); } }
                30 => { if let Some(v) = &mut cur_v { v.z = reader.value().as_f64(); } }
                93 => { reading_faces = true; }
                90 if reading_faces => {
                    let val = reader.value().as_i32();
                    if face_remaining == 0 {
                        face_remaining = val.max(0) as usize;
                        cur_face = Vec::with_capacity(face_remaining);
                    } else {
                        cur_face.push(val);
                        face_remaining -= 1;
                        if face_remaining == 0 {
                            m.faces.push(std::mem::take(&mut cur_face));
                        }
                    }
                }
                94 => { reading_faces = false; }
                _ => {}
            }
        }
        if let Some(v) = cur_v { m.vertexes.push(v); }
        Ok(m)
    }

    fn read_viewport(reader: &mut dyn ICodeValueReader) -> Result<Viewport> {
        let mut vp = Viewport::new();
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut vp.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => vp.center.x = reader.value().as_f64(),
                20 => vp.center.y = reader.value().as_f64(),
                30 => vp.center.z = reader.value().as_f64(),
                40 => vp.width = reader.value().as_f64(),
                41 => vp.height = reader.value().as_f64(),
                68 => vp.stacking = reader.value().as_i16(),
                69 => vp.id = reader.value().as_i16(),
                12 => vp.view_center.x = reader.value().as_f64(),
                22 => vp.view_center.y = reader.value().as_f64(),
                16 => vp.view_direction.x = reader.value().as_f64(),
                26 => vp.view_direction.y = reader.value().as_f64(),
                36 => vp.view_direction.z = reader.value().as_f64(),
                17 => vp.view_target.x = reader.value().as_f64(),
                27 => vp.view_target.y = reader.value().as_f64(),
                37 => vp.view_target.z = reader.value().as_f64(),
                42 => vp.lens_length = reader.value().as_f64(),
                45 => vp.view_height = reader.value().as_f64(),
                51 => vp.twist_angle = reader.value().as_f64(),
                90 => vp.status = ViewportStatusFlags::from_bits_truncate(reader.value().as_i32()),
                _ => {}
            }
        }
        Ok(vp)
    }

    fn read_tolerance(reader: &mut dyn ICodeValueReader) -> Result<Tolerance> {
        let mut t = Tolerance::new("", Vector3::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut t.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                1 => t.raw_string = reader.value().as_str().to_string(),
                3 => t.style = reader.value().as_str().to_string(),
                10 => t.position.x = reader.value().as_f64(),
                20 => t.position.y = reader.value().as_f64(),
                30 => t.position.z = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(t)
    }

    fn read_shape(reader: &mut dyn ICodeValueReader) -> Result<Shape> {
        let mut s = Shape::new("", "", Vector3::ZERO, 1.0);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut s.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                2 => s.name = reader.value().as_str().to_string(),
                10 => s.position.x = reader.value().as_f64(),
                20 => s.position.y = reader.value().as_f64(),
                30 => s.position.z = reader.value().as_f64(),
                39 => s.thickness = reader.value().as_f64(),
                40 => s.size = reader.value().as_f64(),
                41 => s.width_factor = reader.value().as_f64(),
                50 => s.rotation = reader.value().as_f64(),
                51 => s.oblique_angle = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(s)
    }

    fn read_image(reader: &mut dyn ICodeValueReader) -> Result<Image> {
        let mut img = Image::new("", Vector3::ZERO, 1.0, 1.0);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut img.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                10 => img.position.x = reader.value().as_f64(),
                20 => img.position.y = reader.value().as_f64(),
                30 => img.position.z = reader.value().as_f64(),
                340 => img.definition_handle = Some(reader.value().as_str().to_string()),
                70 => img.display_options = ImageDisplayFlags::from_bits_truncate(reader.value().as_i16()),
                280 => img.clipping = reader.value().as_i16() != 0,
                281 => img.brightness = reader.value().as_i16(),
                282 => img.contrast = reader.value().as_i16(),
                283 => img.fade = reader.value().as_i16(),
                _ => {}
            }
        }
        Ok(img)
    }

    fn read_wipeout(reader: &mut dyn ICodeValueReader) -> Result<Wipeout> {
        let mut w = Wipeout::new(ClippingBoundary::rectangular(Vector2::ZERO, Vector2::new(1.0, 1.0)));
        let mut verts = Vec::new();
        let mut cur: Option<Vector2> = None;
        let mut btype = ClippingBoundaryType::Rectangular;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut w.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                71 => {
                    btype = if reader.value().as_i16() == 2 {
                        ClippingBoundaryType::Polygonal
                    } else {
                        ClippingBoundaryType::Rectangular
                    };
                }
                14 => {
                    if let Some(v) = cur.take() { verts.push(v); }
                    cur = Some(Vector2::new(reader.value().as_f64(), 0.0));
                }
                24 => { if let Some(v) = &mut cur { v.y = reader.value().as_f64(); } }
                _ => {}
            }
        }
        if let Some(v) = cur { verts.push(v); }
        if !verts.is_empty() {
            w.clipping_boundary = ClippingBoundary {
                boundary_type: btype,
                vertexes: verts,
            };
        }
        Ok(w)
    }

    fn read_underlay(reader: &mut dyn ICodeValueReader, code: &str) -> Result<Underlay> {
        let utype = match code {
            DxfObjectCode::UNDERLAY_DGN => UnderlayType::DGN,
            DxfObjectCode::UNDERLAY_DWF => UnderlayType::DWF,
            _ => UnderlayType::PDF,
        };
        let mut u = Underlay::new(utype, "");
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut u.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                340 => u.definition_handle = Some(reader.value().as_str().to_string()),
                10 => u.position.x = reader.value().as_f64(),
                20 => u.position.y = reader.value().as_f64(),
                30 => u.position.z = reader.value().as_f64(),
                41 => u.scale.x = reader.value().as_f64(),
                42 => u.scale.y = reader.value().as_f64(),
                50 => u.rotation = reader.value().as_f64(),
                280 => u.display_options = UnderlayDisplayFlags::from_bits_truncate(reader.value().as_i16()),
                281 => u.contrast = reader.value().as_i16(),
                282 => u.fade = reader.value().as_i16(),
                _ => {}
            }
        }
        Ok(u)
    }

    fn read_acis_entity(reader: &mut dyn ICodeValueReader, code: &str) -> Result<EntityObject> {
        let mut common = EntityCommon::new(
            match code {
                DxfObjectCode::BODY => EntityType::Body,
                DxfObjectCode::REGION => EntityType::Region,
                _ => EntityType::Solid3D,
            },
            match code {
                DxfObjectCode::BODY => DxfObjectCode::BODY,
                DxfObjectCode::REGION => DxfObjectCode::REGION,
                _ => DxfObjectCode::SOLID_3D,
            },
        );
        let mut acis = AcisEntityData::new();
        let mut chunks = Vec::new();
        let mut history_handle = None;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                70 => acis.modeler_format_version = reader.value().as_i16(),
                1 | 3 => {
                    if let Ok(chunk) = AcisSatChunk::new(reader.code(), reader.value().as_str()) {
                        chunks.push(chunk);
                    }
                }
                350 => history_handle = Some(reader.value().as_str().to_string()),
                _ => {}
            }
        }
        if !chunks.is_empty() {
            let _ = acis.set_encoded_sat_chunks(chunks);
        }
        Ok(match code {
            DxfObjectCode::BODY => Body { common, acis }.into(),
            DxfObjectCode::REGION => Region { common, acis }.into(),
            _ => Solid3D {
                common,
                acis,
                history_handle,
            }
            .into(),
        })
    }

    fn read_stored_table(reader: &mut dyn ICodeValueReader) -> Result<StoredTable> {
        let mut t = StoredTable::new(0, 0);
        let mut cell_texts = Vec::new();
        let mut row_heights = Vec::new();
        let mut col_widths = Vec::new();
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut t.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                2 => t.block_name = reader.value().as_str().to_string(),
                10 => t.insertion_point.x = reader.value().as_f64(),
                20 => t.insertion_point.y = reader.value().as_f64(),
                30 => t.insertion_point.z = reader.value().as_f64(),
                11 => t.horizontal_direction.x = reader.value().as_f64(),
                21 => t.horizontal_direction.y = reader.value().as_f64(),
                31 => t.horizontal_direction.z = reader.value().as_f64(),
                91 => t.row_count = reader.value().as_i32().max(0) as usize,
                92 => t.column_count = reader.value().as_i32().max(0) as usize,
                141 => row_heights.push(reader.value().as_f64()),
                142 => col_widths.push(reader.value().as_f64()),
                1 => cell_texts.push(reader.value().as_str().to_string()),
                _ => {}
            }
        }
        if !row_heights.is_empty() { t.row_heights = row_heights; }
        if !col_widths.is_empty() { t.column_widths = col_widths; }
        let total = t.row_count * t.column_count;
        t.cells.clear();
        for idx in 0..total {
            let r = idx / t.column_count.max(1);
            let c = idx % t.column_count.max(1);
            let txt = cell_texts.get(idx).cloned().unwrap_or_default();
            t.cells.push(StoredTableCell::text(r, c, txt));
        }
        Ok(t)
    }

    fn read_multileader(reader: &mut dyn ICodeValueReader) -> Result<MultiLeader> {
        let mut ml = MultiLeader::with_mtext("", Vector3::ZERO, Vec::new());
        ml.leaders.clear();
        let mut in_leader = false;
        let mut in_leader_line = false;
        let mut cur_node: Option<MLeaderLeaderNode> = None;
        let mut cur_line: Option<MLeaderLeaderLine> = None;
        let mut cur_pt: Option<Vector3> = None;

        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut ml.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                302 => {
                    if reader.value().as_str().eq_ignore_ascii_case("LEADER{") {
                        in_leader = true;
                        cur_node = Some(MLeaderLeaderNode::default());
                    }
                }
                303 => {
                    if in_leader {
                        if let Some(n) = cur_node.take() { ml.leaders.push(n); }
                        in_leader = false;
                    }
                }
                304 => {
                    if reader.value().as_str().eq_ignore_ascii_case("LEADER_LINE{") {
                        in_leader_line = true;
                        cur_line = Some(MLeaderLeaderLine { points: Vec::new(), index: 0 });
                    } else if !in_leader_line {
                        ml.text = reader.value().as_str().to_string();
                    }
                }
                305 => {
                    if in_leader_line {
                        if let Some(pt) = cur_pt.take() {
                            if let Some(l) = &mut cur_line { l.points.push(pt); }
                        }
                        if let Some(l) = cur_line.take() {
                            if let Some(n) = &mut cur_node { n.leader_lines.push(l); }
                        }
                        in_leader_line = false;
                    }
                }
                340 => ml.style_handle = Some(reader.value().as_str().to_string()),
                172 => {
                    if let Some(ct) = MLeaderContentType::from_i16(reader.value().as_i16()) {
                        ml.content_type = ct;
                    }
                }
                40 => {
                    if in_leader {
                        if let Some(n) = &mut cur_node { n.dogleg_length = reader.value().as_f64(); }
                    } else {
                        ml.content_scale = reader.value().as_f64();
                    }
                }
                41 => ml.text_height = reader.value().as_f64(),
                42 => {
                    if !in_leader { ml.arrowhead_size = reader.value().as_f64(); }
                }
                10 => {
                    if in_leader_line {
                        if let Some(pt) = cur_pt.take() {
                            if let Some(l) = &mut cur_line { l.points.push(pt); }
                        }
                        cur_pt = Some(Vector3::new(reader.value().as_f64(), 0.0, 0.0));
                    } else if in_leader {
                        if let Some(n) = &mut cur_node { n.last_leader_line_point.x = reader.value().as_f64(); }
                    } else {
                        ml.content_base_point.x = reader.value().as_f64();
                    }
                }
                20 => {
                    if in_leader_line {
                        if let Some(pt) = &mut cur_pt { pt.y = reader.value().as_f64(); }
                    } else if in_leader {
                        if let Some(n) = &mut cur_node { n.last_leader_line_point.y = reader.value().as_f64(); }
                    } else {
                        ml.content_base_point.y = reader.value().as_f64();
                    }
                }
                30 => {
                    if in_leader_line {
                        if let Some(pt) = &mut cur_pt { pt.z = reader.value().as_f64(); }
                    } else if in_leader {
                        if let Some(n) = &mut cur_node { n.last_leader_line_point.z = reader.value().as_f64(); }
                    } else {
                        ml.content_base_point.z = reader.value().as_f64();
                    }
                }
                12 => ml.text_location.x = reader.value().as_f64(),
                22 => ml.text_location.y = reader.value().as_f64(),
                32 => ml.text_location.z = reader.value().as_f64(),
                _ => {}
            }
        }
        Ok(ml)
    }

    fn read_light(reader: &mut dyn ICodeValueReader) -> Result<Light> {
        let mut l = Light::new("", LightType::Point, Vector3::ZERO);
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut l.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                1 => l.name = reader.value().as_str().to_string(),
                70 => {
                    if let Some(lt) = LightType::from_i16(reader.value().as_i16()) {
                        l.light_type = lt;
                    }
                }
                290 => l.status = reader.value().as_bool(),
                291 => l.plot_glyph = reader.value().as_bool(),
                40 => l.intensity = reader.value().as_f64(),
                10 => l.position.x = reader.value().as_f64(),
                20 => l.position.y = reader.value().as_f64(),
                30 => l.position.z = reader.value().as_f64(),
                11 => l.target.x = reader.value().as_f64(),
                21 => l.target.y = reader.value().as_f64(),
                31 => l.target.z = reader.value().as_f64(),
                72 => l.attenuation_type = reader.value().as_i16(),
                292 => l.use_attenuation_limits = reader.value().as_bool(),
                41 => l.attenuation_start_limit = reader.value().as_f64(),
                42 => l.attenuation_end_limit = reader.value().as_f64(),
                50 => l.hotspot_angle = reader.value().as_f64(),
                51 => l.falloff_angle = reader.value().as_f64(),
                293 => l.cast_shadows = reader.value().as_bool(),
                _ => {}
            }
        }
        Ok(l)
    }

    fn read_ole2_frame(reader: &mut dyn ICodeValueReader) -> Result<Ole2Frame> {
        let mut f = Ole2Frame::new(Vector3::ZERO, Vector3::ZERO, Vec::new());
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut f.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                70 => f.ole_version = reader.value().as_i16(),
                3 => f.ole_client_type = reader.value().as_str().to_string(),
                10 => f.upper_left_corner.x = reader.value().as_f64(),
                20 => f.upper_left_corner.y = reader.value().as_f64(),
                30 => f.upper_left_corner.z = reader.value().as_f64(),
                11 => f.lower_right_corner.x = reader.value().as_f64(),
                21 => f.lower_right_corner.y = reader.value().as_f64(),
                31 => f.lower_right_corner.z = reader.value().as_f64(),
                71 => f.ole_object_type = reader.value().as_i16(),
                72 => f.tile_mode = reader.value().as_i16(),
                310 => {
                    let b = reader.value().as_bytes();
                    if !b.is_empty() {
                        f.binary_data.extend_from_slice(b);
                    } else if let Some(dec) = decode_hex(reader.value().as_str()) {
                        f.binary_data.extend_from_slice(&dec);
                    }
                }
                _ => {}
            }
        }
        Ok(f)
    }

    fn read_ole_frame(reader: &mut dyn ICodeValueReader) -> Result<OleFrame> {
        let mut f = OleFrame::new(Vec::new());
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut f.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                70 => f.ole_version = reader.value().as_i16(),
                310 => {
                    let b = reader.value().as_bytes();
                    if !b.is_empty() {
                        f.binary_data.extend_from_slice(b);
                    } else if let Some(dec) = decode_hex(reader.value().as_str()) {
                        f.binary_data.extend_from_slice(&dec);
                    }
                }
                _ => {}
            }
        }
        Ok(f)
    }

    fn read_section_entity(reader: &mut dyn ICodeValueReader) -> Result<Section> {
        let mut s = Section::new("", Vec::new());
        let mut cur_v: Option<Vector3> = None;
        while reader.next()? {
            if reader.code() == 0 { break; }
            if Self::apply_common_code(reader, &mut s.common)? { if reader.code() == 0 { break; } continue; }
            match reader.code() {
                90 => s.state = reader.value().as_i32(),
                91 => s.flags = reader.value().as_i32(),
                1 => s.name = reader.value().as_str().to_string(),
                10 => s.vertical_direction.x = reader.value().as_f64(),
                20 => s.vertical_direction.y = reader.value().as_f64(),
                30 => s.vertical_direction.z = reader.value().as_f64(),
                40 => s.top_height = reader.value().as_f64(),
                41 => s.bottom_height = reader.value().as_f64(),
                70 => s.indicator_transparency = reader.value().as_i16(),
                63 => s.indicator_color = AciColor::from_cad_index(reader.value().as_i16()),
                11 => {
                    if let Some(v) = cur_v.take() { s.vertices.push(v); }
                    cur_v = Some(Vector3::new(reader.value().as_f64(), 0.0, 0.0));
                }
                21 => { if let Some(v) = &mut cur_v { v.y = reader.value().as_f64(); } }
                31 => { if let Some(v) = &mut cur_v { v.z = reader.value().as_f64(); } }
                _ => {}
            }
        }
        if let Some(v) = cur_v { s.vertices.push(v); }
        Ok(s)
    }

    fn read_opaque(reader: &mut dyn ICodeValueReader, entity_name: &str) -> Result<DxfOpaqueEntity> {
        let mut tags = Vec::new();
        let mut common = EntityCommon::new(EntityType::Opaque, "");
        common.object.code_name = entity_name.to_string();
        while reader.next()? {
            if reader.code() == 0 { break; }
            let c = reader.code();
            let v = reader.value().to_string_value();
            if c == 5 { common.object.handle = Some(v.clone()); }
            if c == 8 { common.layer = v.clone(); }
            tags.push(DxfTag::new(c, v));
        }
        let mut o = DxfOpaqueEntity::new(entity_name, tags);
        o.common = common;
        Ok(o)
    }
}
