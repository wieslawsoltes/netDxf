use std::io::Write;

use crate::blocks::Block;
use crate::coordinate_system::CoordinateSystem;
use crate::dxf_document::DxfDocument;
use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::entities::*;
use crate::error::Result;
use crate::header::{HeaderVariableCode, HeaderVariableValue};
use crate::io::binary_code_value_writer::BinaryCodeValueWriter;
use crate::io::code_value_writer::ICodeValueWriter;
use crate::io::text_code_value_writer::TextCodeValueWriter;
use crate::lineweight::Lineweight;
use crate::math_helper::MathHelper;
use crate::subclass_marker::SubclassMarker;
use crate::tables::*;
use crate::units::DrawingTime;
use crate::x_data_code::XDataCode;
use crate::x_data_record::XDataValue;

/// High-performance DXF file writer supporting ASCII and Binary formats (`netDxf.IO.DxfWriter`).
pub struct DxfWriter;

impl DxfWriter {
    pub fn write<W: Write>(doc: &DxfDocument, writer: W, is_binary: bool) -> Result<()> {
        if is_binary {
            let mut bw = BinaryCodeValueWriter::new(writer, false)?;
            Self::write_document(doc, &mut bw)?;
            bw.flush()
        } else {
            let mut tw = TextCodeValueWriter::new(writer);
            Self::write_document(doc, &mut tw)?;
            tw.flush()
        }
    }

    fn write_document(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        for comment in &doc.comments {
            w.write_str(999, comment)?;
        }

        Self::write_header(doc, w)?;
        Self::write_classes(doc, w)?;
        Self::write_tables(doc, w)?;
        Self::write_blocks(doc, w)?;
        Self::write_entities(doc, w)?;
        Self::write_objects(doc, w)?;
        if !doc.thumbnail_image.is_empty() {
            Self::write_thumbnail(doc, w)?;
        }

        w.write_str(0, DxfObjectCode::END_OF_FILE)?;
        Ok(())
    }

    fn begin_section(w: &mut dyn ICodeValueWriter, name: &str) -> Result<()> {
        w.write_str(0, DxfObjectCode::BEGIN_SECTION)?;
        w.write_str(2, name)
    }

    fn end_section(w: &mut dyn ICodeValueWriter) -> Result<()> {
        w.write_str(0, DxfObjectCode::END_SECTION)
    }

    fn write_header(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        let hv = &doc.drawing_variables;
        Self::begin_section(w, DxfObjectCode::HEADER_SECTION)?;

        w.write_str(9, HeaderVariableCode::ACAD_VER)?;
        w.write_str(1, hv.acad_ver.as_code())?;

        w.write_str(9, HeaderVariableCode::DWG_CODE_PAGE)?;
        w.write_str(3, &hv.dwg_code_page)?;

        w.write_str(9, HeaderVariableCode::LAST_SAVED_BY)?;
        w.write_str(1, &hv.last_saved_by)?;

        w.write_str(9, HeaderVariableCode::HANDLE_SEED)?;
        w.write_str(5, &hv.handle_seed)?;

        w.write_str(9, HeaderVariableCode::ANGBASE)?;
        w.write_f64(50, hv.angbase)?;

        w.write_str(9, HeaderVariableCode::ANGDIR)?;
        w.write_i16(70, hv.angdir as i16)?;

        w.write_str(9, HeaderVariableCode::ATT_MODE)?;
        w.write_i16(70, hv.att_mode as i16)?;

        w.write_str(9, HeaderVariableCode::A_UNITS)?;
        w.write_i16(70, hv.a_units as i16)?;

        w.write_str(9, HeaderVariableCode::A_UPREC)?;
        w.write_i16(70, hv.a_uprec)?;

        w.write_str(9, HeaderVariableCode::CE_COLOR)?;
        w.write_i16(62, hv.ce_color.index)?;

        w.write_str(9, HeaderVariableCode::CE_LT_SCALE)?;
        w.write_f64(40, hv.ce_lt_scale)?;

        w.write_str(9, HeaderVariableCode::CE_LTYPE)?;
        w.write_str(6, &hv.ce_ltype)?;

        w.write_str(9, HeaderVariableCode::CE_LWEIGHT)?;
        w.write_i16(370, hv.ce_lweight.as_i16())?;

        w.write_str(9, HeaderVariableCode::C_LAYER)?;
        w.write_str(8, &hv.c_layer)?;

        w.write_str(9, HeaderVariableCode::CML_JUST)?;
        w.write_i16(70, hv.cml_just as i16)?;

        w.write_str(9, HeaderVariableCode::CML_SCALE)?;
        w.write_f64(40, hv.cml_scale)?;

        w.write_str(9, HeaderVariableCode::CML_STYLE)?;
        w.write_str(2, &hv.cml_style)?;

        w.write_str(9, HeaderVariableCode::DIM_STYLE)?;
        w.write_str(2, &hv.dim_style)?;

        w.write_str(9, HeaderVariableCode::TEXT_SIZE)?;
        w.write_f64(40, hv.text_size)?;

        w.write_str(9, HeaderVariableCode::TEXT_STYLE)?;
        w.write_str(7, &hv.text_style)?;

        w.write_str(9, HeaderVariableCode::L_UNITS)?;
        w.write_i16(70, hv.l_units as i16)?;

        w.write_str(9, HeaderVariableCode::L_UPREC)?;
        w.write_i16(70, hv.l_uprec)?;

        w.write_str(9, HeaderVariableCode::MIRR_TEXT)?;
        w.write_i16(70, i16::from(hv.mirr_text))?;

        w.write_str(9, HeaderVariableCode::EXTNAMES)?;
        w.write_bool(290, hv.extnames)?;

        w.write_str(9, HeaderVariableCode::INS_BASE)?;
        w.write_f64(10, hv.ins_base.x)?;
        w.write_f64(20, hv.ins_base.y)?;
        w.write_f64(30, hv.ins_base.z)?;

        w.write_str(9, HeaderVariableCode::INS_UNITS)?;
        w.write_i16(70, hv.ins_units as i16)?;

        w.write_str(9, HeaderVariableCode::LT_SCALE)?;
        w.write_f64(40, hv.lt_scale)?;

        w.write_str(9, HeaderVariableCode::LW_DISPLAY)?;
        w.write_bool(290, hv.lw_display)?;

        w.write_str(9, HeaderVariableCode::PD_MODE)?;
        w.write_i16(70, hv.pd_mode as i16)?;

        w.write_str(9, HeaderVariableCode::PD_SIZE)?;
        w.write_f64(40, hv.pd_size)?;

        w.write_str(9, HeaderVariableCode::P_LINE_GEN)?;
        w.write_i16(70, hv.p_line_gen)?;

        w.write_str(9, HeaderVariableCode::PS_LT_SCALE)?;
        w.write_i16(70, hv.ps_lt_scale)?;

        w.write_str(9, HeaderVariableCode::SPLINE_SEGS)?;
        w.write_i16(70, hv.spline_segs)?;

        w.write_str(9, HeaderVariableCode::SURF_U)?;
        w.write_i16(70, hv.surf_u)?;

        w.write_str(9, HeaderVariableCode::SURF_V)?;
        w.write_i16(70, hv.surf_v)?;

        w.write_str(9, HeaderVariableCode::TD_CREATE)?;
        w.write_f64(40, DrawingTime::to_julian_calendar(hv.td_create))?;

        w.write_str(9, HeaderVariableCode::TDU_CREATE)?;
        w.write_f64(40, DrawingTime::to_julian_calendar(hv.tdu_create))?;

        w.write_str(9, HeaderVariableCode::TD_UPDATE)?;
        w.write_f64(40, DrawingTime::to_julian_calendar(hv.td_update))?;

        w.write_str(9, HeaderVariableCode::TDU_UPDATE)?;
        w.write_f64(40, DrawingTime::to_julian_calendar(hv.tdu_update))?;

        w.write_str(9, HeaderVariableCode::TDIN_DWG)?;
        w.write_f64(40, DrawingTime::to_elapsed_days(hv.tdin_dwg))?;

        w.write_str(9, HeaderVariableCode::UCS_ORG)?;
        w.write_f64(10, hv.ucs_org.x)?;
        w.write_f64(20, hv.ucs_org.y)?;
        w.write_f64(30, hv.ucs_org.z)?;

        w.write_str(9, HeaderVariableCode::UCS_X_DIR)?;
        w.write_f64(10, hv.ucs_x_dir.x)?;
        w.write_f64(20, hv.ucs_x_dir.y)?;
        w.write_f64(30, hv.ucs_x_dir.z)?;

        w.write_str(9, HeaderVariableCode::UCS_Y_DIR)?;
        w.write_f64(10, hv.ucs_y_dir.x)?;
        w.write_f64(20, hv.ucs_y_dir.y)?;
        w.write_f64(30, hv.ucs_y_dir.z)?;

        for var in hv.custom_variables.values() {
            w.write_str(9, &var.name)?;
            match &var.value {
                HeaderVariableValue::String(s) => w.write_str(var.group_code, s)?,
                HeaderVariableValue::Int16(v) => w.write_i16(var.group_code, *v)?,
                HeaderVariableValue::Int32(v) => w.write_i32(var.group_code, *v)?,
                HeaderVariableValue::Float(v) => w.write_f64(var.group_code, *v)?,
                HeaderVariableValue::Bool(v) => w.write_bool(var.group_code, *v)?,
                HeaderVariableValue::Vector3(v) => {
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, v.z)?;
                }
            }
        }

        Self::end_section(w)
    }

    fn write_classes(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        Self::begin_section(w, DxfObjectCode::CLASSES_SECTION)?;
        for cls in &doc.classes {
            w.write_str(0, DxfObjectCode::CLASS)?;
            w.write_str(1, &cls.dxf_name)?;
            w.write_str(2, &cls.cpp_class_name)?;
            w.write_str(3, &cls.application_name)?;
            w.write_i32(90, cls.proxy_capabilities)?;
            w.write_i32(91, cls.instance_count)?;
            w.write_i16(280, i16::from(cls.was_a_proxy))?;
            w.write_i16(281, i16::from(cls.is_an_entity))?;
        }
        Self::end_section(w)
    }

    fn begin_table(w: &mut dyn ICodeValueWriter, name: &str, handle: &str, count: usize) -> Result<()> {
        w.write_str(0, DxfObjectCode::TABLE)?;
        w.write_str(2, name)?;
        w.write_str(5, handle)?;
        w.write_str(330, "0")?;
        w.write_str(100, SubclassMarker::TABLE)?;
        w.write_i16(70, count as i16)
    }

    fn end_table(w: &mut dyn ICodeValueWriter) -> Result<()> {
        w.write_str(0, DxfObjectCode::END_TABLE)
    }

    fn write_tables(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        Self::begin_section(w, DxfObjectCode::TABLES_SECTION)?;

        // VPORT
        Self::begin_table(w, DxfObjectCode::VPORT_TABLE, "8", doc.vports.len())?;
        for vp in doc.vports.iter() {
            w.write_str(0, DxfObjectCode::VPORT)?;
            if let Some(h) = vp.handle() { w.write_str(5, h)?; }
            w.write_str(330, "8")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::VPORT)?;
            w.write_str(2, vp.name())?;
            w.write_i16(70, 0)?;
            w.write_f64(10, 0.0)?;
            w.write_f64(20, 0.0)?;
            w.write_f64(11, 1.0)?;
            w.write_f64(21, 1.0)?;
            w.write_f64(12, vp.view_center.x)?;
            w.write_f64(22, vp.view_center.y)?;
            w.write_f64(13, vp.snap_base_point.x)?;
            w.write_f64(23, vp.snap_base_point.y)?;
            w.write_f64(14, vp.snap_spacing.x)?;
            w.write_f64(24, vp.snap_spacing.y)?;
            w.write_f64(15, vp.grid_spacing.x)?;
            w.write_f64(25, vp.grid_spacing.y)?;
            w.write_f64(16, vp.direction.x)?;
            w.write_f64(26, vp.direction.y)?;
            w.write_f64(36, vp.direction.z)?;
            w.write_f64(17, vp.target.x)?;
            w.write_f64(27, vp.target.y)?;
            w.write_f64(37, vp.target.z)?;
            w.write_f64(40, vp.view_height)?;
            w.write_f64(41, vp.view_aspect_ratio)?;
            w.write_i16(75, i16::from(vp.snap_mode))?;
            w.write_i16(76, i16::from(vp.show_grid))?;
        }
        Self::end_table(w)?;

        // LTYPE
        Self::begin_table(w, DxfObjectCode::LINETYPE_TABLE, "5", doc.linetypes.len())?;
        for lt in doc.linetypes.iter() {
            w.write_str(0, DxfObjectCode::LINETYPE)?;
            if let Some(h) = lt.handle() { w.write_str(5, h)?; }
            w.write_str(330, "5")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::LINETYPE)?;
            w.write_str(2, lt.name())?;
            w.write_i16(70, 0)?;
            w.write_str(3, &lt.description)?;
            w.write_i16(72, 65)?;
            w.write_i16(73, lt.segments.len() as i16)?;
            w.write_f64(40, lt.length())?;
            for seg in &lt.segments {
                w.write_f64(49, seg.length())?;
                w.write_i16(74, 0)?;
            }
        }
        Self::end_table(w)?;

        // LAYER
        Self::begin_table(w, DxfObjectCode::LAYER_TABLE, "2", doc.layers.len())?;
        for layer in doc.layers.iter() {
            w.write_str(0, DxfObjectCode::LAYER)?;
            if let Some(h) = layer.handle() { w.write_str(5, h)?; }
            w.write_str(330, "2")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::LAYER)?;
            w.write_str(2, layer.name())?;
            w.write_i16(70, layer.flags().bits())?;
            let color_idx = if layer.is_visible {
                layer.color.index.abs()
            } else {
                -layer.color.index.abs()
            };
            w.write_i16(62, color_idx)?;
            if layer.color.use_true_color {
                w.write_i32(420, layer.color.to_true_color())?;
            }
            w.write_str(6, &layer.linetype)?;
            w.write_bool(290, layer.plot)?;
            w.write_i16(370, layer.lineweight.as_i16())?;
            if !layer.transparency.is_by_layer() && layer.transparency.value() != 0 {
                w.write_i32(440, layer.transparency.to_alpha_value())?;
            }
            Self::write_xdata(&layer.table.object, w)?;
        }
        Self::end_table(w)?;

        // STYLE
        Self::begin_table(
            w,
            DxfObjectCode::TEXT_STYLE_TABLE,
            "3",
            doc.text_styles.len() + doc.shape_styles.len(),
        )?;
        for ts in doc.text_styles.iter() {
            w.write_str(0, DxfObjectCode::TEXT_STYLE)?;
            if let Some(h) = ts.handle() { w.write_str(5, h)?; }
            w.write_str(330, "3")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::TEXT_STYLE)?;
            w.write_str(2, ts.name())?;
            w.write_i16(70, if ts.is_vertical { 4 } else { 0 })?;
            w.write_f64(40, ts.height)?;
            w.write_f64(41, ts.width_factor)?;
            w.write_f64(50, ts.oblique_angle)?;
            let gen = (if ts.is_backward { 2 } else { 0 }) | (if ts.is_upside_down { 4 } else { 0 });
            w.write_i16(71, gen)?;
            w.write_f64(42, ts.height.max(1.0))?;
            w.write_str(3, &ts.font_file)?;
            w.write_str(4, &ts.big_font)?;
        }
        for ss in doc.shape_styles.iter() {
            w.write_str(0, DxfObjectCode::TEXT_STYLE)?;
            if let Some(h) = ss.handle() { w.write_str(5, h)?; }
            w.write_str(330, "3")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::TEXT_STYLE)?;
            w.write_str(2, ss.name())?;
            w.write_i16(70, 1)?;
            w.write_f64(40, ss.size)?;
            w.write_f64(41, ss.width_factor)?;
            w.write_f64(50, ss.oblique_angle)?;
            w.write_i16(71, 0)?;
            w.write_f64(42, ss.size)?;
            w.write_str(3, &ss.file)?;
            w.write_str(4, "")?;
        }
        Self::end_table(w)?;

        // VIEW
        Self::begin_table(w, DxfObjectCode::VIEW_TABLE, "6", doc.views.len())?;
        for view in doc.views.iter() {
            w.write_str(0, DxfObjectCode::VIEW)?;
            if let Some(h) = view.handle() { w.write_str(5, h)?; }
            w.write_str(330, "6")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::VIEW)?;
            w.write_str(2, view.name())?;
            w.write_i16(70, 0)?;
            w.write_f64(40, view.height)?;
            w.write_f64(41, view.width)?;
            w.write_f64(11, view.camera.x)?;
            w.write_f64(21, view.camera.y)?;
            w.write_f64(31, view.camera.z)?;
            w.write_f64(12, view.target.x)?;
            w.write_f64(22, view.target.y)?;
            w.write_f64(32, view.target.z)?;
        }
        Self::end_table(w)?;

        // UCS
        Self::begin_table(w, DxfObjectCode::UCS_TABLE, "7", doc.ucss.len())?;
        for ucs in doc.ucss.iter() {
            w.write_str(0, DxfObjectCode::UCS)?;
            if let Some(h) = ucs.handle() { w.write_str(5, h)?; }
            w.write_str(330, "7")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::UCS)?;
            w.write_str(2, ucs.name())?;
            w.write_i16(70, 0)?;
            w.write_f64(10, ucs.origin.x)?;
            w.write_f64(20, ucs.origin.y)?;
            w.write_f64(30, ucs.origin.z)?;
            w.write_f64(11, ucs.x_axis.x)?;
            w.write_f64(21, ucs.x_axis.y)?;
            w.write_f64(31, ucs.x_axis.z)?;
            w.write_f64(12, ucs.y_axis.x)?;
            w.write_f64(22, ucs.y_axis.y)?;
            w.write_f64(32, ucs.y_axis.z)?;
            w.write_f64(146, ucs.elevation)?;
        }
        Self::end_table(w)?;

        // APPID
        Self::begin_table(
            w,
            DxfObjectCode::APPLICATION_ID_TABLE,
            "9",
            doc.application_registries.len(),
        )?;
        for app in doc.application_registries.iter() {
            w.write_str(0, DxfObjectCode::APP_ID)?;
            if let Some(h) = app.handle() { w.write_str(5, h)?; }
            w.write_str(330, "9")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::APPLICATION_ID)?;
            w.write_str(2, app.name())?;
            w.write_i16(70, 0)?;
        }
        Self::end_table(w)?;

        // DIMSTYLE
        Self::begin_table(
            w,
            DxfObjectCode::DIMENSION_STYLE_TABLE,
            "A",
            doc.dimension_styles.len(),
        )?;
        w.write_str(100, SubclassMarker::DIMENSION_STYLE_TABLE)?;
        for ds in doc.dimension_styles.iter() {
            w.write_str(0, DxfObjectCode::DIM_STYLE)?;
            if let Some(h) = ds.handle() { w.write_str(105, h)?; }
            w.write_str(330, "A")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::DIMENSION_STYLE)?;
            w.write_str(2, ds.name())?;
            w.write_i16(70, 0)?;
            w.write_f64(40, ds.dim_scale_overall)?;
            w.write_f64(41, ds.arrow_size)?;
            w.write_f64(42, ds.ext_line_offset)?;
            w.write_f64(43, ds.dim_baseline_spacing)?;
            w.write_f64(44, ds.ext_line_extend)?;
            w.write_f64(45, ds.dim_roundoff)?;
            w.write_f64(46, ds.dim_line_extend)?;
            w.write_f64(140, ds.text_height)?;
            w.write_f64(141, ds.center_mark_size)?;
            w.write_f64(144, ds.dim_scale_linear)?;
            w.write_f64(147, ds.text_offset)?;
            w.write_i16(176, ds.dim_line_color.index)?;
            w.write_i16(177, ds.ext_line_color.index)?;
            w.write_i16(178, ds.text_color.index)?;
            w.write_i16(179, ds.angular_precision)?;
            w.write_i16(271, ds.length_precision)?;
        }
        Self::end_table(w)?;

        // BLOCK_RECORD
        Self::begin_table(w, DxfObjectCode::BLOCK_RECORD_TABLE, "1", doc.blocks.len())?;
        for blk in doc.blocks.iter() {
            w.write_str(0, DxfObjectCode::BLOCK_RECORD)?;
            if let Some(h) = blk.record.object.handle.as_deref() {
                w.write_str(5, h)?;
            }
            w.write_str(330, "1")?;
            w.write_str(100, SubclassMarker::TABLE_RECORD)?;
            w.write_str(100, SubclassMarker::BLOCK_RECORD)?;
            w.write_str(2, blk.name())?;
            w.write_i16(70, blk.record.units as i16)?;
            w.write_i16(280, i16::from(blk.record.allow_exploding))?;
            w.write_i16(281, i16::from(blk.record.scale_uniformly))?;
        }
        Self::end_table(w)?;

        Self::end_section(w)
    }

    fn write_blocks(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        Self::begin_section(w, DxfObjectCode::BLOCKS_SECTION)?;
        for blk in doc.blocks.iter() {
            Self::write_block(blk, w)?;
        }
        Self::end_section(w)
    }

    fn write_block(blk: &Block, w: &mut dyn ICodeValueWriter) -> Result<()> {
        let owner = blk.record.object.handle.as_deref().unwrap_or("0");
        w.write_str(0, DxfObjectCode::BEGIN_BLOCK)?;
        if let Some(h) = blk.handle() { w.write_str(5, h)?; }
        w.write_str(330, owner)?;
        w.write_str(100, SubclassMarker::ENTITY)?;
        w.write_str(8, &blk.layer)?;
        w.write_str(100, SubclassMarker::BLOCK_BEGIN)?;
        w.write_str(2, blk.name())?;
        w.write_i16(70, blk.flags.bits())?;
        w.write_f64(10, blk.origin.x)?;
        w.write_f64(20, blk.origin.y)?;
        w.write_f64(30, blk.origin.z)?;
        w.write_str(3, blk.name())?;
        w.write_str(1, &blk.xref_file)?;
        if !blk.description.is_empty() {
            w.write_str(4, &blk.description)?;
        }

        for ent in &blk.entities {
            Self::write_entity(ent, owner, w)?;
        }

        for ad in blk.attribute_definitions.values() {
            w.write_str(0, DxfObjectCode::ATTRIBUTE_DEFINITION)?;
            if let Some(h) = ad.object.handle.as_deref() { w.write_str(5, h)?; }
            w.write_str(330, owner)?;
            w.write_str(100, SubclassMarker::ENTITY)?;
            w.write_str(8, &ad.layer)?;
            w.write_str(100, SubclassMarker::TEXT)?;
            w.write_f64(10, ad.position.x)?;
            w.write_f64(20, ad.position.y)?;
            w.write_f64(30, ad.position.z)?;
            w.write_f64(40, ad.height)?;
            w.write_str(1, &ad.value)?;
            w.write_f64(50, ad.rotation)?;
            w.write_str(7, &ad.style)?;
            w.write_str(100, SubclassMarker::ATTRIBUTE_DEFINITION)?;
            w.write_str(3, &ad.prompt)?;
            w.write_str(2, &ad.tag)?;
            w.write_i16(70, ad.flags.bits())?;
        }

        w.write_str(0, DxfObjectCode::END_BLOCK)?;
        if let Some(h) = blk.end.object.handle.as_deref() { w.write_str(5, h)?; }
        w.write_str(330, owner)?;
        w.write_str(100, SubclassMarker::ENTITY)?;
        w.write_str(8, &blk.end.layer)?;
        w.write_str(100, SubclassMarker::BLOCK_END)?;
        Ok(())
    }

    fn write_entities(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        Self::begin_section(w, DxfObjectCode::ENTITIES_SECTION)?;
        let owner = doc
            .blocks
            .get(Block::MODEL_SPACE_NAME)
            .and_then(|b| b.record.object.handle.as_deref())
            .unwrap_or("1F");
        for ent in doc.entities.iter() {
            Self::write_entity(ent, owner, w)?;
        }
        Self::end_section(w)
    }

    fn write_entity_common(c: &EntityCommon, owner: &str, w: &mut dyn ICodeValueWriter) -> Result<()> {
        if let Some(h) = c.handle() {
            w.write_str(5, h)?;
        }
        w.write_str(330, c.object.owner_handle.as_deref().unwrap_or(owner))?;
        w.write_str(100, SubclassMarker::ENTITY)?;
        w.write_str(8, &c.layer)?;
        if !c.linetype.eq_ignore_ascii_case(Linetype::BY_LAYER_NAME) {
            w.write_str(6, &c.linetype)?;
        }
        if !c.color.is_by_layer() {
            w.write_i16(62, c.color.index)?;
        }
        if c.color.use_true_color {
            w.write_i32(420, c.color.to_true_color())?;
        }
        if !MathHelper::is_one(c.linetype_scale) {
            w.write_f64(48, c.linetype_scale)?;
        }
        if !c.is_visible {
            w.write_i16(60, 1)?;
        }
        if c.lineweight != Lineweight::ByLayer {
            w.write_i16(370, c.lineweight.as_i16())?;
        }
        if !c.transparency.is_by_layer() {
            w.write_i32(440, c.transparency.to_alpha_value())?;
        }
        Ok(())
    }

    fn write_normal(normal: crate::vector3::Vector3, w: &mut dyn ICodeValueWriter) -> Result<()> {
        if normal != crate::vector3::Vector3::UNIT_Z {
            w.write_f64(210, normal.x)?;
            w.write_f64(220, normal.y)?;
            w.write_f64(230, normal.z)?;
        }
        Ok(())
    }

    fn write_xdata(obj: &DxfObject, w: &mut dyn ICodeValueWriter) -> Result<()> {
        for xd in obj.x_data.values() {
            w.write_str(XDataCode::AppReg.as_i16(), &xd.application_registry)?;
            for rec in &xd.x_data_record {
                let code = rec.code.as_i16();
                match &rec.value {
                    XDataValue::String(s) => w.write_str(code, s)?,
                    XDataValue::Binary(b) => w.write_bytes(code, b)?,
                    XDataValue::Real(v) => w.write_f64(code, *v)?,
                    XDataValue::Int16(v) => w.write_i16(code, *v)?,
                    XDataValue::Int32(v) => w.write_i32(code, *v)?,
                }
            }
        }
        Ok(())
    }

    fn write_entity(ent: &EntityObject, owner: &str, w: &mut dyn ICodeValueWriter) -> Result<()> {
        match ent {
            EntityObject::Point(p) => {
                w.write_str(0, DxfObjectCode::POINT)?;
                Self::write_entity_common(&p.common, owner, w)?;
                w.write_str(100, SubclassMarker::POINT)?;
                w.write_f64(10, p.position.x)?;
                w.write_f64(20, p.position.y)?;
                w.write_f64(30, p.position.z)?;
                if !MathHelper::is_zero(p.thickness) { w.write_f64(39, p.thickness)?; }
                Self::write_normal(p.common.normal, w)?;
                if !MathHelper::is_zero(p.rotation) { w.write_f64(50, p.rotation)?; }
                Self::write_xdata(&p.common.object, w)?;
            }
            EntityObject::Line(l) => {
                w.write_str(0, DxfObjectCode::LINE)?;
                Self::write_entity_common(&l.common, owner, w)?;
                w.write_str(100, SubclassMarker::LINE)?;
                w.write_f64(10, l.start_point.x)?;
                w.write_f64(20, l.start_point.y)?;
                w.write_f64(30, l.start_point.z)?;
                w.write_f64(11, l.end_point.x)?;
                w.write_f64(21, l.end_point.y)?;
                w.write_f64(31, l.end_point.z)?;
                if !MathHelper::is_zero(l.thickness) { w.write_f64(39, l.thickness)?; }
                Self::write_normal(l.common.normal, w)?;
                Self::write_xdata(&l.common.object, w)?;
            }
            EntityObject::Ray(r) => {
                w.write_str(0, DxfObjectCode::RAY)?;
                Self::write_entity_common(&r.common, owner, w)?;
                w.write_str(100, SubclassMarker::RAY)?;
                w.write_f64(10, r.origin.x)?;
                w.write_f64(20, r.origin.y)?;
                w.write_f64(30, r.origin.z)?;
                w.write_f64(11, r.direction.x)?;
                w.write_f64(21, r.direction.y)?;
                w.write_f64(31, r.direction.z)?;
                Self::write_xdata(&r.common.object, w)?;
            }
            EntityObject::XLine(x) => {
                w.write_str(0, DxfObjectCode::XLINE)?;
                Self::write_entity_common(&x.common, owner, w)?;
                w.write_str(100, SubclassMarker::XLINE)?;
                w.write_f64(10, x.origin.x)?;
                w.write_f64(20, x.origin.y)?;
                w.write_f64(30, x.origin.z)?;
                w.write_f64(11, x.direction.x)?;
                w.write_f64(21, x.direction.y)?;
                w.write_f64(31, x.direction.z)?;
                Self::write_xdata(&x.common.object, w)?;
            }
            EntityObject::Circle(c) => {
                w.write_str(0, DxfObjectCode::CIRCLE)?;
                Self::write_entity_common(&c.common, owner, w)?;
                w.write_str(100, SubclassMarker::CIRCLE)?;
                let ocs = MathHelper::transform_3d(
                    c.center,
                    c.common.normal,
                    CoordinateSystem::World,
                    CoordinateSystem::Object,
                );
                w.write_f64(10, ocs.x)?;
                w.write_f64(20, ocs.y)?;
                w.write_f64(30, ocs.z)?;
                w.write_f64(40, c.radius)?;
                if !MathHelper::is_zero(c.thickness) { w.write_f64(39, c.thickness)?; }
                Self::write_normal(c.common.normal, w)?;
                Self::write_xdata(&c.common.object, w)?;
            }
            EntityObject::Arc(a) => {
                w.write_str(0, DxfObjectCode::ARC)?;
                Self::write_entity_common(&a.common, owner, w)?;
                w.write_str(100, SubclassMarker::CIRCLE)?;
                let ocs = MathHelper::transform_3d(
                    a.center,
                    a.common.normal,
                    CoordinateSystem::World,
                    CoordinateSystem::Object,
                );
                w.write_f64(10, ocs.x)?;
                w.write_f64(20, ocs.y)?;
                w.write_f64(30, ocs.z)?;
                w.write_f64(40, a.radius)?;
                if !MathHelper::is_zero(a.thickness) { w.write_f64(39, a.thickness)?; }
                w.write_str(100, SubclassMarker::ARC)?;
                w.write_f64(50, a.start_angle)?;
                w.write_f64(51, a.end_angle)?;
                Self::write_normal(a.common.normal, w)?;
                Self::write_xdata(&a.common.object, w)?;
            }
            EntityObject::Ellipse(e) => {
                w.write_str(0, DxfObjectCode::ELLIPSE)?;
                Self::write_entity_common(&e.common, owner, w)?;
                w.write_str(100, SubclassMarker::ELLIPSE)?;
                w.write_f64(10, e.center.x)?;
                w.write_f64(20, e.center.y)?;
                w.write_f64(30, e.center.z)?;
                let maj = e.major_axis_endpoint_wcs();
                w.write_f64(11, maj.x)?;
                w.write_f64(21, maj.y)?;
                w.write_f64(31, maj.z)?;
                Self::write_normal(e.common.normal, w)?;
                w.write_f64(40, e.minor_to_major_ratio())?;
                if e.is_full_ellipse() {
                    w.write_f64(41, 0.0)?;
                    w.write_f64(42, MathHelper::TWO_PI)?;
                } else {
                    w.write_f64(41, e.start_angle * MathHelper::DEG_TO_RAD)?;
                    w.write_f64(42, e.end_angle * MathHelper::DEG_TO_RAD)?;
                }
                Self::write_xdata(&e.common.object, w)?;
            }
            EntityObject::Solid(s) => {
                w.write_str(0, DxfObjectCode::SOLID)?;
                Self::write_entity_common(&s.common, owner, w)?;
                w.write_str(100, SubclassMarker::SOLID)?;
                w.write_f64(10, s.first_vertex.x)?;
                w.write_f64(20, s.first_vertex.y)?;
                w.write_f64(30, s.elevation)?;
                w.write_f64(11, s.second_vertex.x)?;
                w.write_f64(21, s.second_vertex.y)?;
                w.write_f64(31, s.elevation)?;
                w.write_f64(12, s.third_vertex.x)?;
                w.write_f64(22, s.third_vertex.y)?;
                w.write_f64(32, s.elevation)?;
                w.write_f64(13, s.fourth_vertex.x)?;
                w.write_f64(23, s.fourth_vertex.y)?;
                w.write_f64(33, s.elevation)?;
                if !MathHelper::is_zero(s.thickness) { w.write_f64(39, s.thickness)?; }
                Self::write_normal(s.common.normal, w)?;
                Self::write_xdata(&s.common.object, w)?;
            }
            EntityObject::Trace(t) => {
                w.write_str(0, DxfObjectCode::TRACE)?;
                Self::write_entity_common(&t.common, owner, w)?;
                w.write_str(100, SubclassMarker::TRACE)?;
                w.write_f64(10, t.first_vertex.x)?;
                w.write_f64(20, t.first_vertex.y)?;
                w.write_f64(30, t.elevation)?;
                w.write_f64(11, t.second_vertex.x)?;
                w.write_f64(21, t.second_vertex.y)?;
                w.write_f64(31, t.elevation)?;
                w.write_f64(12, t.third_vertex.x)?;
                w.write_f64(22, t.third_vertex.y)?;
                w.write_f64(32, t.elevation)?;
                w.write_f64(13, t.fourth_vertex.x)?;
                w.write_f64(23, t.fourth_vertex.y)?;
                w.write_f64(33, t.elevation)?;
                if !MathHelper::is_zero(t.thickness) { w.write_f64(39, t.thickness)?; }
                Self::write_normal(t.common.normal, w)?;
                Self::write_xdata(&t.common.object, w)?;
            }
            EntityObject::Face3D(f) => {
                w.write_str(0, DxfObjectCode::FACE_3D)?;
                Self::write_entity_common(&f.common, owner, w)?;
                w.write_str(100, SubclassMarker::FACE_3D)?;
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
                Self::write_xdata(&f.common.object, w)?;
            }
            EntityObject::Text(t) => {
                w.write_str(0, DxfObjectCode::TEXT)?;
                Self::write_entity_common(&t.common, owner, w)?;
                w.write_str(100, SubclassMarker::TEXT)?;
                let ocs = MathHelper::transform_3d(
                    t.position,
                    t.common.normal,
                    CoordinateSystem::World,
                    CoordinateSystem::Object,
                );
                w.write_f64(10, ocs.x)?;
                w.write_f64(20, ocs.y)?;
                w.write_f64(30, ocs.z)?;
                w.write_f64(40, t.height)?;
                w.write_str(1, &t.value)?;
                if !MathHelper::is_zero(t.rotation) { w.write_f64(50, t.rotation)?; }
                if !MathHelper::is_one(t.width_factor) { w.write_f64(41, t.width_factor)?; }
                if !MathHelper::is_zero(t.oblique_angle) { w.write_f64(51, t.oblique_angle)?; }
                w.write_str(7, &t.style)?;
                let gen = (if t.is_backward { 2 } else { 0 }) | (if t.is_upside_down { 4 } else { 0 });
                if gen != 0 { w.write_i16(71, gen)?; }
                let (h_align, v_align) = t.alignment.to_dxf_codes();
                if h_align != 0 { w.write_i16(72, h_align)?; }
                if t.alignment != TextAlignment::BaselineLeft {
                    w.write_f64(11, ocs.x)?;
                    w.write_f64(21, ocs.y)?;
                    w.write_f64(31, ocs.z)?;
                }
                Self::write_normal(t.common.normal, w)?;
                w.write_str(100, SubclassMarker::TEXT)?;
                if v_align != 0 { w.write_i16(73, v_align)?; }
                Self::write_xdata(&t.common.object, w)?;
            }
            EntityObject::MText(mt) => {
                w.write_str(0, DxfObjectCode::MTEXT)?;
                Self::write_entity_common(&mt.common, owner, w)?;
                w.write_str(100, SubclassMarker::MTEXT)?;
                w.write_f64(10, mt.position.x)?;
                w.write_f64(20, mt.position.y)?;
                w.write_f64(30, mt.position.z)?;
                w.write_f64(40, mt.height)?;
                w.write_f64(41, mt.rectangle_width)?;
                w.write_i16(71, mt.attachment_point as i16)?;
                w.write_i16(72, mt.drawing_direction as i16)?;
                // Split long MText into 250-char code 3 chunks + final code 1
                let chars: Vec<char> = mt.value.chars().collect();
                if chars.len() <= 250 {
                    w.write_str(1, &mt.value)?;
                } else {
                    let mut idx = 0;
                    while idx + 250 < chars.len() {
                        let chunk: String = chars[idx..idx + 250].iter().collect();
                        w.write_str(3, &chunk)?;
                        idx += 250;
                    }
                    let rest: String = chars[idx..].iter().collect();
                    w.write_str(1, &rest)?;
                }
                w.write_str(7, &mt.style)?;
                Self::write_normal(mt.common.normal, w)?;
                if !MathHelper::is_zero(mt.rotation) { w.write_f64(50, mt.rotation)?; }
                w.write_i16(73, mt.line_spacing_style as i16)?;
                w.write_f64(44, mt.line_spacing_factor)?;
                Self::write_xdata(&mt.common.object, w)?;
            }
            EntityObject::Polyline2D(pl) => {
                w.write_str(0, DxfObjectCode::LW_POLYLINE)?;
                Self::write_entity_common(&pl.common, owner, w)?;
                w.write_str(100, SubclassMarker::POLYLINE)?;
                w.write_i32(90, pl.vertexes.len() as i32)?;
                let flags = (if pl.is_closed { 1 } else { 0 })
                    | (if pl.linetype_generation { 128 } else { 0 });
                w.write_i16(70, flags)?;
                if !MathHelper::is_zero(pl.elevation) { w.write_f64(38, pl.elevation)?; }
                if !MathHelper::is_zero(pl.thickness) { w.write_f64(39, pl.thickness)?; }
                for v in &pl.vertexes {
                    w.write_f64(10, v.position.x)?;
                    w.write_f64(20, v.position.y)?;
                    if !MathHelper::is_zero(v.start_width) { w.write_f64(40, v.start_width)?; }
                    if !MathHelper::is_zero(v.end_width) { w.write_f64(41, v.end_width)?; }
                    if !MathHelper::is_zero(v.bulge) { w.write_f64(42, v.bulge)?; }
                }
                Self::write_normal(pl.common.normal, w)?;
                Self::write_xdata(&pl.common.object, w)?;
            }
            EntityObject::Polyline3D(pl) => {
                w.write_str(0, DxfObjectCode::POLYLINE)?;
                Self::write_entity_common(&pl.common, owner, w)?;
                w.write_str(100, SubclassMarker::POLYLINE_3D)?;
                w.write_i16(66, 1)?;
                w.write_f64(10, 0.0)?;
                w.write_f64(20, 0.0)?;
                w.write_f64(30, 0.0)?;
                let mut flags = PolylineTypeFlags::POLYLINE_3D;
                if pl.is_closed {
                    flags |= PolylineTypeFlags::CLOSED_POLYLINE_OR_CLOSED_POLYGON_MESH_IN_M;
                }
                if pl.linetype_generation {
                    flags |= PolylineTypeFlags::CONTINUOUS_LINETYPE_PATTERN;
                }
                w.write_i16(70, flags.bits())?;
                w.write_i16(75, pl.smooth_type as i16)?;
                Self::write_xdata(&pl.common.object, w)?;
                let parent = pl.common.handle().unwrap_or(owner);
                for v in &pl.vertexes {
                    w.write_str(0, DxfObjectCode::VERTEX)?;
                    w.write_str(330, parent)?;
                    w.write_str(100, SubclassMarker::ENTITY)?;
                    w.write_str(8, &pl.common.layer)?;
                    w.write_str(100, SubclassMarker::VERTEX)?;
                    w.write_str(100, SubclassMarker::POLYLINE_3D_VERTEX)?;
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, v.z)?;
                    w.write_i16(70, 32)?;
                }
                w.write_str(0, DxfObjectCode::END_SEQUENCE)?;
                w.write_str(330, parent)?;
                w.write_str(100, SubclassMarker::ENTITY)?;
                w.write_str(8, &pl.common.layer)?;
            }
            EntityObject::Spline(s) => {
                w.write_str(0, DxfObjectCode::SPLINE)?;
                Self::write_entity_common(&s.common, owner, w)?;
                w.write_str(100, SubclassMarker::SPLINE)?;
                Self::write_normal(s.common.normal, w)?;
                w.write_i16(70, s.flags.bits())?;
                w.write_i16(71, s.degree)?;
                w.write_i16(72, s.knots.len() as i16)?;
                w.write_i16(73, s.control_points.len() as i16)?;
                w.write_i16(74, s.fit_points.len() as i16)?;
                w.write_f64(42, s.knot_tolerance)?;
                w.write_f64(43, s.control_point_tolerance)?;
                w.write_f64(44, s.fit_tolerance)?;
                if let Some(st) = s.start_tangent {
                    w.write_f64(12, st.x)?;
                    w.write_f64(22, st.y)?;
                    w.write_f64(32, st.z)?;
                }
                if let Some(et) = s.end_tangent {
                    w.write_f64(13, et.x)?;
                    w.write_f64(23, et.y)?;
                    w.write_f64(33, et.z)?;
                }
                for &k in &s.knots {
                    w.write_f64(40, k)?;
                }
                for &wt in &s.weights {
                    w.write_f64(41, wt)?;
                }
                for p in &s.control_points {
                    w.write_f64(10, p.x)?;
                    w.write_f64(20, p.y)?;
                    w.write_f64(30, p.z)?;
                }
                for p in &s.fit_points {
                    w.write_f64(11, p.x)?;
                    w.write_f64(21, p.y)?;
                    w.write_f64(31, p.z)?;
                }
                Self::write_xdata(&s.common.object, w)?;
            }
            EntityObject::Hatch(h) => {
                w.write_str(0, DxfObjectCode::HATCH)?;
                Self::write_entity_common(&h.common, owner, w)?;
                w.write_str(100, SubclassMarker::HATCH)?;
                w.write_f64(10, 0.0)?;
                w.write_f64(20, 0.0)?;
                w.write_f64(30, h.elevation)?;
                Self::write_normal(h.common.normal, w)?;
                w.write_str(2, &h.pattern.name)?;
                w.write_i16(70, h.pattern.fill as i16)?;
                w.write_i16(71, i16::from(h.associative))?;
                w.write_i32(91, h.boundary_paths.len() as i32)?;
                for path in &h.boundary_paths {
                    w.write_i32(92, path.path_type_flags.bits())?;
                    if path.path_type_flags.contains(HatchBoundaryPathTypeFlags::POLYLINE) {
                        if let Some(HatchBoundaryPathEdge::Polyline { vertexes, is_closed }) =
                            path.edges.first()
                        {
                            let has_bulge = vertexes.iter().any(|(_, b)| !MathHelper::is_zero(*b));
                            w.write_i16(72, i16::from(has_bulge))?;
                            w.write_i16(73, i16::from(*is_closed))?;
                            w.write_i32(93, vertexes.len() as i32)?;
                            for (pt, bulge) in vertexes {
                                w.write_f64(10, pt.x)?;
                                w.write_f64(20, pt.y)?;
                                if has_bulge {
                                    w.write_f64(42, *bulge)?;
                                }
                            }
                        }
                    } else {
                        w.write_i32(93, path.edges.len() as i32)?;
                        for edge in &path.edges {
                            match edge {
                                HatchBoundaryPathEdge::Line { start, end } => {
                                    w.write_i16(72, 1)?;
                                    w.write_f64(10, start.x)?;
                                    w.write_f64(20, start.y)?;
                                    w.write_f64(11, end.x)?;
                                    w.write_f64(21, end.y)?;
                                }
                                HatchBoundaryPathEdge::Arc {
                                    center,
                                    radius,
                                    start_angle,
                                    end_angle,
                                    is_counter_clockwise,
                                } => {
                                    w.write_i16(72, 2)?;
                                    w.write_f64(10, center.x)?;
                                    w.write_f64(20, center.y)?;
                                    w.write_f64(40, *radius)?;
                                    w.write_f64(50, *start_angle)?;
                                    w.write_f64(51, *end_angle)?;
                                    w.write_i16(73, i16::from(*is_counter_clockwise))?;
                                }
                                _ => {}
                            }
                        }
                    }
                    w.write_i32(97, 0)?;
                }
                w.write_i16(75, h.pattern.style as i16)?;
                w.write_i16(76, h.pattern.hatch_type as i16)?;
                if h.pattern.fill == HatchFillType::PatternFill {
                    w.write_f64(52, h.pattern.angle)?;
                    w.write_f64(41, h.pattern.scale)?;
                    w.write_i16(77, 0)?;
                    w.write_i16(78, h.pattern.line_definitions.len() as i16)?;
                }
                w.write_i32(98, h.seed_points.len() as i32)?;
                for sp in &h.seed_points {
                    w.write_f64(10, sp.x)?;
                    w.write_f64(20, sp.y)?;
                }
                Self::write_xdata(&h.common.object, w)?;
            }
            EntityObject::Insert(ins) => {
                w.write_str(0, DxfObjectCode::INSERT)?;
                Self::write_entity_common(&ins.common, owner, w)?;
                w.write_str(100, SubclassMarker::INSERT)?;
                if !ins.attributes.is_empty() {
                    w.write_i16(66, 1)?;
                }
                w.write_str(2, &ins.block_name)?;
                let ocs = MathHelper::transform_3d(
                    ins.position,
                    ins.common.normal,
                    CoordinateSystem::World,
                    CoordinateSystem::Object,
                );
                w.write_f64(10, ocs.x)?;
                w.write_f64(20, ocs.y)?;
                w.write_f64(30, ocs.z)?;
                w.write_f64(41, ins.scale.x)?;
                w.write_f64(42, ins.scale.y)?;
                w.write_f64(43, ins.scale.z)?;
                if !MathHelper::is_zero(ins.rotation) { w.write_f64(50, ins.rotation)?; }
                if ins.column_count > 1 { w.write_i16(70, ins.column_count)?; }
                if ins.row_count > 1 { w.write_i16(71, ins.row_count)?; }
                if !MathHelper::is_zero(ins.column_spacing) { w.write_f64(44, ins.column_spacing)?; }
                if !MathHelper::is_zero(ins.row_spacing) { w.write_f64(45, ins.row_spacing)?; }
                Self::write_normal(ins.common.normal, w)?;
                Self::write_xdata(&ins.common.object, w)?;
                if !ins.attributes.is_empty() {
                    let parent = ins.common.handle().unwrap_or(owner);
                    for a in &ins.attributes {
                        w.write_str(0, DxfObjectCode::ATTRIBUTE)?;
                        if let Some(h) = a.object.handle.as_deref() { w.write_str(5, h)?; }
                        w.write_str(330, parent)?;
                        w.write_str(100, SubclassMarker::ENTITY)?;
                        w.write_str(8, &a.layer)?;
                        w.write_str(100, SubclassMarker::TEXT)?;
                        w.write_f64(10, a.position.x)?;
                        w.write_f64(20, a.position.y)?;
                        w.write_f64(30, a.position.z)?;
                        w.write_f64(40, a.height)?;
                        w.write_str(1, &a.value)?;
                        w.write_f64(50, a.rotation)?;
                        w.write_str(7, &a.style)?;
                        w.write_str(100, SubclassMarker::ATTRIBUTE)?;
                        w.write_str(2, &a.tag)?;
                        w.write_i16(70, a.flags.bits())?;
                    }
                    w.write_str(0, DxfObjectCode::END_SEQUENCE)?;
                    w.write_str(330, parent)?;
                    w.write_str(100, SubclassMarker::ENTITY)?;
                    w.write_str(8, &ins.common.layer)?;
                }
            }
            EntityObject::Dimension(d) => {
                w.write_str(0, DxfObjectCode::DIMENSION)?;
                Self::write_entity_common(&d.common, owner, w)?;
                w.write_str(100, SubclassMarker::DIMENSION)?;
                if !d.block_name.is_empty() { w.write_str(2, &d.block_name)?; }
                w.write_f64(10, d.definition_point.x)?;
                w.write_f64(20, d.definition_point.y)?;
                w.write_f64(30, d.definition_point.z)?;
                w.write_f64(11, d.text_reference_point.x)?;
                w.write_f64(21, d.text_reference_point.y)?;
                w.write_f64(31, d.elevation)?;
                w.write_i16(70, d.dimension_type as i16 | 32)?;
                w.write_i16(71, d.attachment_point as i16)?;
                w.write_i16(72, d.line_spacing_style as i16)?;
                w.write_f64(41, d.line_spacing_factor)?;
                w.write_f64(42, d.measurement)?;
                if let Some(ut) = &d.user_text { w.write_str(1, ut)?; }
                w.write_f64(53, d.text_rotation)?;
                w.write_str(3, &d.style)?;
                Self::write_normal(d.common.normal, w)?;
                match d.dimension_type {
                    DimensionType::Linear | DimensionType::Aligned => {
                        w.write_str(100, SubclassMarker::ALIGNED_DIMENSION)?;
                        w.write_f64(13, d.first_reference_point.x)?;
                        w.write_f64(23, d.first_reference_point.y)?;
                        w.write_f64(33, d.first_reference_point.z)?;
                        w.write_f64(14, d.second_reference_point.x)?;
                        w.write_f64(24, d.second_reference_point.y)?;
                        w.write_f64(34, d.second_reference_point.z)?;
                        if d.dimension_type == DimensionType::Linear {
                            w.write_f64(50, d.rotation)?;
                            w.write_str(100, SubclassMarker::LINEAR_DIMENSION)?;
                        }
                    }
                    DimensionType::Radius => {
                        w.write_str(100, SubclassMarker::RADIAL_DIMENSION)?;
                        w.write_f64(15, d.first_reference_point.x)?;
                        w.write_f64(25, d.first_reference_point.y)?;
                        w.write_f64(35, d.first_reference_point.z)?;
                        w.write_f64(40, d.leader_length)?;
                    }
                    DimensionType::Diameter => {
                        w.write_str(100, SubclassMarker::DIAMETRIC_DIMENSION)?;
                        w.write_f64(15, d.first_reference_point.x)?;
                        w.write_f64(25, d.first_reference_point.y)?;
                        w.write_f64(35, d.first_reference_point.z)?;
                        w.write_f64(40, d.leader_length)?;
                    }
                    _ => {}
                }
                Self::write_xdata(&d.common.object, w)?;
            }
            EntityObject::Leader(l) => {
                w.write_str(0, DxfObjectCode::LEADER)?;
                Self::write_entity_common(&l.common, owner, w)?;
                w.write_str(100, SubclassMarker::LEADER)?;
                w.write_str(3, &l.style)?;
                w.write_i16(71, i16::from(l.show_arrowhead))?;
                w.write_i16(72, l.path_type as i16)?;
                w.write_i16(75, i16::from(l.has_hookline))?;
                w.write_i16(76, l.vertexes.len() as i16)?;
                for v in &l.vertexes {
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, l.elevation)?;
                }
                if let Some(ah) = &l.annotation_handle {
                    w.write_str(340, ah)?;
                }
                Self::write_normal(l.common.normal, w)?;
                Self::write_xdata(&l.common.object, w)?;
            }
            EntityObject::MLine(ml) => {
                w.write_str(0, DxfObjectCode::MLINE)?;
                Self::write_entity_common(&ml.common, owner, w)?;
                w.write_str(100, SubclassMarker::MLINE)?;
                w.write_str(2, &ml.style)?;
                w.write_f64(40, ml.scale)?;
                w.write_i16(70, ml.justification as i16)?;
                w.write_i16(71, ml.flags.bits())?;
                w.write_i16(72, ml.vertexes.len() as i16)?;
                for v in &ml.vertexes {
                    w.write_f64(11, v.position.x)?;
                    w.write_f64(21, v.position.y)?;
                    w.write_f64(31, ml.elevation)?;
                    w.write_f64(12, v.direction.x)?;
                    w.write_f64(22, v.direction.y)?;
                    w.write_f64(32, 0.0)?;
                    w.write_f64(13, v.miter.x)?;
                    w.write_f64(23, v.miter.y)?;
                    w.write_f64(33, 0.0)?;
                }
                Self::write_xdata(&ml.common.object, w)?;
            }
            EntityObject::Mesh(m) => {
                w.write_str(0, DxfObjectCode::MESH)?;
                Self::write_entity_common(&m.common, owner, w)?;
                w.write_str(100, SubclassMarker::MESH)?;
                w.write_i16(71, 2)?;
                w.write_i16(72, m.subdivision_level as i16)?;
                w.write_i32(92, m.vertexes.len() as i32)?;
                for v in &m.vertexes {
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, v.z)?;
                }
                let face_list_len: usize = m.faces.iter().map(|f| f.len() + 1).sum();
                w.write_i32(93, face_list_len as i32)?;
                for f in &m.faces {
                    w.write_i32(90, f.len() as i32)?;
                    for &idx in f {
                        w.write_i32(90, idx)?;
                    }
                }
                w.write_i32(94, 0)?;
                w.write_i32(95, 0)?;
                Self::write_xdata(&m.common.object, w)?;
            }
            EntityObject::PolyfaceMesh(pf) => {
                w.write_str(0, DxfObjectCode::POLYLINE)?;
                Self::write_entity_common(&pf.common, owner, w)?;
                w.write_str(100, SubclassMarker::POLYFACE_MESH)?;
                w.write_i16(66, 1)?;
                w.write_f64(10, 0.0)?;
                w.write_f64(20, 0.0)?;
                w.write_f64(30, 0.0)?;
                w.write_i16(70, PolylineTypeFlags::POLYFACE_MESH.bits())?;
                w.write_i16(71, pf.vertexes.len() as i16)?;
                w.write_i16(72, pf.faces.len() as i16)?;
                let parent = pf.common.handle().unwrap_or(owner);
                for v in &pf.vertexes {
                    w.write_str(0, DxfObjectCode::VERTEX)?;
                    w.write_str(330, parent)?;
                    w.write_str(100, SubclassMarker::ENTITY)?;
                    w.write_str(8, &pf.common.layer)?;
                    w.write_str(100, SubclassMarker::VERTEX)?;
                    w.write_str(100, SubclassMarker::POLYFACE_MESH_VERTEX)?;
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, v.z)?;
                    w.write_i16(70, 192)?;
                }
                for f in &pf.faces {
                    w.write_str(0, DxfObjectCode::VERTEX)?;
                    w.write_str(330, parent)?;
                    w.write_str(100, SubclassMarker::ENTITY)?;
                    w.write_str(8, &pf.common.layer)?;
                    w.write_str(100, SubclassMarker::POLYFACE_MESH_FACE)?;
                    w.write_f64(10, 0.0)?;
                    w.write_f64(20, 0.0)?;
                    w.write_f64(30, 0.0)?;
                    w.write_i16(70, 128)?;
                    for (i, &idx) in f.vertex_indexes.iter().take(4).enumerate() {
                        w.write_i16(71 + i as i16, idx)?;
                    }
                }
                w.write_str(0, DxfObjectCode::END_SEQUENCE)?;
                w.write_str(330, parent)?;
                w.write_str(100, SubclassMarker::ENTITY)?;
                w.write_str(8, &pf.common.layer)?;
            }
            EntityObject::PolygonMesh(pm) => {
                w.write_str(0, DxfObjectCode::POLYLINE)?;
                Self::write_entity_common(&pm.common, owner, w)?;
                w.write_str(100, SubclassMarker::POLYGON_MESH)?;
                w.write_i16(66, 1)?;
                w.write_f64(10, 0.0)?;
                w.write_f64(20, 0.0)?;
                w.write_f64(30, 0.0)?;
                let mut flags = PolylineTypeFlags::POLYGON_MESH_3D;
                if pm.is_closed_in_m {
                    flags |= PolylineTypeFlags::CLOSED_POLYLINE_OR_CLOSED_POLYGON_MESH_IN_M;
                }
                if pm.is_closed_in_n {
                    flags |= PolylineTypeFlags::CLOSED_POLYGON_MESH_IN_N;
                }
                w.write_i16(70, flags.bits())?;
                w.write_i16(71, pm.m)?;
                w.write_i16(72, pm.n)?;
                w.write_i16(73, pm.density_m)?;
                w.write_i16(74, pm.density_n)?;
                w.write_i16(75, pm.smooth_type as i16)?;
                let parent = pm.common.handle().unwrap_or(owner);
                for v in &pm.vertexes {
                    w.write_str(0, DxfObjectCode::VERTEX)?;
                    w.write_str(330, parent)?;
                    w.write_str(100, SubclassMarker::ENTITY)?;
                    w.write_str(8, &pm.common.layer)?;
                    w.write_str(100, SubclassMarker::VERTEX)?;
                    w.write_str(100, SubclassMarker::POLYGON_MESH_VERTEX)?;
                    w.write_f64(10, v.x)?;
                    w.write_f64(20, v.y)?;
                    w.write_f64(30, v.z)?;
                    w.write_i16(70, 64)?;
                }
                w.write_str(0, DxfObjectCode::END_SEQUENCE)?;
                w.write_str(330, parent)?;
                w.write_str(100, SubclassMarker::ENTITY)?;
                w.write_str(8, &pm.common.layer)?;
            }
            EntityObject::Viewport(vp) => {
                w.write_str(0, DxfObjectCode::VIEWPORT)?;
                Self::write_entity_common(&vp.common, owner, w)?;
                w.write_str(100, SubclassMarker::VIEWPORT)?;
                w.write_f64(10, vp.center.x)?;
                w.write_f64(20, vp.center.y)?;
                w.write_f64(30, vp.center.z)?;
                w.write_f64(40, vp.width)?;
                w.write_f64(41, vp.height)?;
                w.write_i16(68, vp.stacking)?;
                w.write_i16(69, vp.id)?;
                w.write_f64(12, vp.view_center.x)?;
                w.write_f64(22, vp.view_center.y)?;
                w.write_f64(16, vp.view_direction.x)?;
                w.write_f64(26, vp.view_direction.y)?;
                w.write_f64(36, vp.view_direction.z)?;
                w.write_f64(17, vp.view_target.x)?;
                w.write_f64(27, vp.view_target.y)?;
                w.write_f64(37, vp.view_target.z)?;
                w.write_f64(42, vp.lens_length)?;
                w.write_f64(45, vp.view_height)?;
                w.write_f64(51, vp.twist_angle)?;
                w.write_i32(90, vp.status.bits())?;
                Self::write_xdata(&vp.common.object, w)?;
            }
            EntityObject::Tolerance(t) => {
                w.write_str(0, DxfObjectCode::TOLERANCE)?;
                Self::write_entity_common(&t.common, owner, w)?;
                w.write_str(100, SubclassMarker::TOLERANCE)?;
                w.write_str(3, &t.style)?;
                w.write_f64(10, t.position.x)?;
                w.write_f64(20, t.position.y)?;
                w.write_f64(30, t.position.z)?;
                w.write_str(1, &t.raw_string)?;
                Self::write_xdata(&t.common.object, w)?;
            }
            EntityObject::Shape(s) => {
                w.write_str(0, DxfObjectCode::SHAPE)?;
                Self::write_entity_common(&s.common, owner, w)?;
                w.write_str(100, SubclassMarker::SHAPE)?;
                w.write_f64(10, s.position.x)?;
                w.write_f64(20, s.position.y)?;
                w.write_f64(30, s.position.z)?;
                w.write_f64(40, s.size)?;
                w.write_str(2, &s.name)?;
                w.write_f64(50, s.rotation)?;
                w.write_f64(41, s.width_factor)?;
                w.write_f64(51, s.oblique_angle)?;
                Self::write_xdata(&s.common.object, w)?;
            }
            EntityObject::Image(img) => {
                w.write_str(0, DxfObjectCode::IMAGE)?;
                Self::write_entity_common(&img.common, owner, w)?;
                w.write_str(100, SubclassMarker::RASTER_IMAGE)?;
                w.write_f64(10, img.position.x)?;
                w.write_f64(20, img.position.y)?;
                w.write_f64(30, img.position.z)?;
                if let Some(h) = &img.definition_handle { w.write_str(340, h)?; }
                w.write_i16(70, img.display_options.bits())?;
                w.write_i16(280, i16::from(img.clipping))?;
                w.write_i16(281, img.brightness)?;
                w.write_i16(282, img.contrast)?;
                w.write_i16(283, img.fade)?;
                Self::write_xdata(&img.common.object, w)?;
            }
            EntityObject::Underlay(u) => {
                w.write_str(0, u.underlay_type.entity_code())?;
                Self::write_entity_common(&u.common, owner, w)?;
                w.write_str(100, SubclassMarker::UNDERLAY)?;
                if let Some(h) = &u.definition_handle { w.write_str(340, h)?; }
                w.write_f64(10, u.position.x)?;
                w.write_f64(20, u.position.y)?;
                w.write_f64(30, u.position.z)?;
                w.write_f64(41, u.scale.x)?;
                w.write_f64(42, u.scale.y)?;
                w.write_f64(50, u.rotation)?;
                w.write_i16(280, u.display_options.bits())?;
                w.write_i16(281, u.contrast)?;
                w.write_i16(282, u.fade)?;
                Self::write_xdata(&u.common.object, w)?;
            }
            EntityObject::Wipeout(wp) => {
                w.write_str(0, DxfObjectCode::WIPEOUT)?;
                Self::write_entity_common(&wp.common, owner, w)?;
                w.write_str(100, SubclassMarker::WIPEOUT)?;
                w.write_i16(71, wp.clipping_boundary.boundary_type as i16)?;
                w.write_i32(91, wp.clipping_boundary.vertexes.len() as i32)?;
                for v in &wp.clipping_boundary.vertexes {
                    w.write_f64(14, v.x)?;
                    w.write_f64(24, v.y)?;
                }
                Self::write_xdata(&wp.common.object, w)?;
            }
            EntityObject::Helix(hx) => {
                w.write_str(0, DxfObjectCode::HELIX)?;
                Self::write_entity_common(&hx.common, owner, w)?;
                w.write_str(100, SubclassMarker::HELIX)?;
                w.write_f64(10, hx.axis_base_point.x)?;
                w.write_f64(20, hx.axis_base_point.y)?;
                w.write_f64(30, hx.axis_base_point.z)?;
                w.write_f64(11, hx.start_point.x)?;
                w.write_f64(21, hx.start_point.y)?;
                w.write_f64(31, hx.start_point.z)?;
                w.write_f64(12, hx.axis_vector.x)?;
                w.write_f64(22, hx.axis_vector.y)?;
                w.write_f64(32, hx.axis_vector.z)?;
                w.write_f64(40, hx.radius)?;
                w.write_f64(41, hx.turns)?;
                w.write_f64(42, hx.turn_height)?;
                w.write_bool(290, hx.handedness)?;
                w.write_i16(280, hx.constrain)?;
                Self::write_xdata(&hx.common.object, w)?;
            }
            EntityObject::Solid3D(s) => {
                w.write_str(0, DxfObjectCode::SOLID_3D)?;
                Self::write_entity_common(&s.common, owner, w)?;
                w.write_str(100, SubclassMarker::MODELER_GEOMETRY)?;
                w.write_i16(70, s.acis.modeler_format_version)?;
                for chunk in s.acis.encoded_sat_chunks() {
                    w.write_str(chunk.group_code, &chunk.text)?;
                }
                if let Some(h) = &s.history_handle {
                    w.write_str(100, SubclassMarker::SOLID_3D)?;
                    w.write_str(350, h)?;
                }
                Self::write_xdata(&s.common.object, w)?;
            }
            EntityObject::Body(b) => {
                w.write_str(0, DxfObjectCode::BODY)?;
                Self::write_entity_common(&b.common, owner, w)?;
                w.write_str(100, SubclassMarker::MODELER_GEOMETRY)?;
                w.write_i16(70, b.acis.modeler_format_version)?;
                for chunk in b.acis.encoded_sat_chunks() {
                    w.write_str(chunk.group_code, &chunk.text)?;
                }
                Self::write_xdata(&b.common.object, w)?;
            }
            EntityObject::Region(r) => {
                w.write_str(0, DxfObjectCode::REGION)?;
                Self::write_entity_common(&r.common, owner, w)?;
                w.write_str(100, SubclassMarker::MODELER_GEOMETRY)?;
                w.write_i16(70, r.acis.modeler_format_version)?;
                for chunk in r.acis.encoded_sat_chunks() {
                    w.write_str(chunk.group_code, &chunk.text)?;
                }
                Self::write_xdata(&r.common.object, w)?;
            }
            EntityObject::Table(t) => {
                w.write_str(0, DxfObjectCode::ACAD_TABLE)?;
                Self::write_entity_common(&t.common, owner, w)?;
                w.write_str(100, SubclassMarker::INSERT)?;
                if !t.block_name.is_empty() { w.write_str(2, &t.block_name)?; }
                w.write_f64(10, t.insertion_point.x)?;
                w.write_f64(20, t.insertion_point.y)?;
                w.write_f64(30, t.insertion_point.z)?;
                w.write_str(100, "AcDbTable")?;
                w.write_i16(280, 0)?;
                w.write_f64(11, t.horizontal_direction.x)?;
                w.write_f64(21, t.horizontal_direction.y)?;
                w.write_f64(31, t.horizontal_direction.z)?;
                w.write_i32(91, t.row_count as i32)?;
                w.write_i32(92, t.column_count as i32)?;
                for &rh in &t.row_heights { w.write_f64(141, rh)?; }
                for &cw in &t.column_widths { w.write_f64(142, cw)?; }
                for cell in &t.cells {
                    w.write_i16(171, cell.cell_type)?;
                    w.write_str(1, &cell.text)?;
                }
                Self::write_xdata(&t.common.object, w)?;
            }
            EntityObject::MultiLeader(ml) => {
                w.write_str(0, DxfObjectCode::MULTILEADER)?;
                Self::write_entity_common(&ml.common, owner, w)?;
                w.write_str(100, "AcDbMLeader")?;
                if let Some(sh) = &ml.style_handle { w.write_str(340, sh)?; }
                w.write_i16(172, ml.content_type as i16)?;
                w.write_f64(42, ml.arrowhead_size)?;
                w.write_str(300, "CONTEXT_DATA{")?;
                w.write_f64(40, ml.content_scale)?;
                w.write_f64(10, ml.content_base_point.x)?;
                w.write_f64(20, ml.content_base_point.y)?;
                w.write_f64(30, ml.content_base_point.z)?;
                w.write_f64(41, ml.text_height)?;
                w.write_str(304, &ml.text)?;
                w.write_f64(12, ml.text_location.x)?;
                w.write_f64(22, ml.text_location.y)?;
                w.write_f64(32, ml.text_location.z)?;
                for node in &ml.leaders {
                    w.write_str(302, "LEADER{")?;
                    w.write_f64(10, node.last_leader_line_point.x)?;
                    w.write_f64(20, node.last_leader_line_point.y)?;
                    w.write_f64(30, node.last_leader_line_point.z)?;
                    w.write_f64(40, node.dogleg_length)?;
                    for line in &node.leader_lines {
                        w.write_str(304, "LEADER_LINE{")?;
                        for pt in &line.points {
                            w.write_f64(10, pt.x)?;
                            w.write_f64(20, pt.y)?;
                            w.write_f64(30, pt.z)?;
                        }
                        w.write_str(305, "}")?;
                    }
                    w.write_str(303, "}")?;
                }
                w.write_str(301, "}")?;
                Self::write_xdata(&ml.common.object, w)?;
            }
            EntityObject::Light(l) => {
                w.write_str(0, DxfObjectCode::LIGHT)?;
                Self::write_entity_common(&l.common, owner, w)?;
                w.write_str(100, SubclassMarker::LIGHT)?;
                w.write_str(1, &l.name)?;
                w.write_i16(70, l.light_type as i16)?;
                w.write_bool(290, l.status)?;
                w.write_bool(291, l.plot_glyph)?;
                w.write_f64(40, l.intensity)?;
                w.write_f64(10, l.position.x)?;
                w.write_f64(20, l.position.y)?;
                w.write_f64(30, l.position.z)?;
                w.write_f64(11, l.target.x)?;
                w.write_f64(21, l.target.y)?;
                w.write_f64(31, l.target.z)?;
                w.write_i16(72, l.attenuation_type)?;
                w.write_bool(292, l.use_attenuation_limits)?;
                w.write_f64(41, l.attenuation_start_limit)?;
                w.write_f64(42, l.attenuation_end_limit)?;
                w.write_f64(50, l.hotspot_angle)?;
                w.write_f64(51, l.falloff_angle)?;
                w.write_bool(293, l.cast_shadows)?;
                Self::write_xdata(&l.common.object, w)?;
            }
            EntityObject::Ole2Frame(f) => {
                w.write_str(0, DxfObjectCode::OLE2_FRAME)?;
                Self::write_entity_common(&f.common, owner, w)?;
                w.write_str(100, SubclassMarker::OLE2_FRAME)?;
                w.write_i16(70, f.ole_version)?;
                w.write_str(3, &f.ole_client_type)?;
                w.write_f64(10, f.upper_left_corner.x)?;
                w.write_f64(20, f.upper_left_corner.y)?;
                w.write_f64(30, f.upper_left_corner.z)?;
                w.write_f64(11, f.lower_right_corner.x)?;
                w.write_f64(21, f.lower_right_corner.y)?;
                w.write_f64(31, f.lower_right_corner.z)?;
                w.write_i16(71, f.ole_object_type)?;
                w.write_i16(72, f.tile_mode)?;
                w.write_i32(90, f.binary_data.len() as i32)?;
                for chunk in f.binary_data.chunks(127) {
                    w.write_bytes(310, chunk)?;
                }
                Self::write_xdata(&f.common.object, w)?;
            }
            EntityObject::OleFrame(f) => {
                w.write_str(0, DxfObjectCode::OLE_FRAME)?;
                Self::write_entity_common(&f.common, owner, w)?;
                w.write_str(100, SubclassMarker::OLE_FRAME)?;
                w.write_i16(70, f.ole_version)?;
                w.write_i32(90, f.binary_data.len() as i32)?;
                for chunk in f.binary_data.chunks(127) {
                    w.write_bytes(310, chunk)?;
                }
                Self::write_xdata(&f.common.object, w)?;
            }
            EntityObject::Section(s) => {
                w.write_str(0, DxfObjectCode::BEGIN_SECTION)?;
                Self::write_entity_common(&s.common, owner, w)?;
                w.write_str(100, "AcDbSection")?;
                w.write_i32(90, s.state)?;
                w.write_i32(91, s.flags)?;
                w.write_str(1, &s.name)?;
                w.write_f64(10, s.vertical_direction.x)?;
                w.write_f64(20, s.vertical_direction.y)?;
                w.write_f64(30, s.vertical_direction.z)?;
                w.write_f64(40, s.top_height)?;
                w.write_f64(41, s.bottom_height)?;
                w.write_i16(70, s.indicator_transparency)?;
                w.write_i16(63, s.indicator_color.index)?;
                w.write_i32(92, s.vertices.len() as i32)?;
                for v in &s.vertices {
                    w.write_f64(11, v.x)?;
                    w.write_f64(21, v.y)?;
                    w.write_f64(31, v.z)?;
                }
                Self::write_xdata(&s.common.object, w)?;
            }
            EntityObject::Opaque(o) => {
                w.write_str(0, &o.entity_name)?;
                for tag in &o.raw_tags {
                    w.write_str(tag.code, &tag.value)?;
                }
            }
        }
        Ok(())
    }

    fn write_objects(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        Self::begin_section(w, DxfObjectCode::OBJECTS_SECTION)?;

        // Root Dictionary (handle C)
        w.write_str(0, DxfObjectCode::DICTIONARY)?;
        w.write_str(5, "C")?;
        w.write_str(330, "0")?;
        w.write_str(100, SubclassMarker::DICTIONARY)?;
        w.write_i16(281, 1)?;
        w.write_str(3, DxfObjectCode::GROUP_DICTIONARY)?;
        w.write_str(350, "D")?;
        w.write_str(3, DxfObjectCode::LAYOUT_DICTIONARY)?;
        w.write_str(350, "1A")?;
        w.write_str(3, DxfObjectCode::MLINE_STYLE_DICTIONARY)?;
        w.write_str(350, "17")?;

        // ACAD_GROUP dictionary (handle D)
        w.write_str(0, DxfObjectCode::DICTIONARY)?;
        w.write_str(5, "D")?;
        w.write_str(330, "C")?;
        w.write_str(100, SubclassMarker::DICTIONARY)?;
        w.write_i16(281, 1)?;
        for g in doc.groups.iter() {
            if let Some(h) = g.handle() {
                w.write_str(3, g.name())?;
                w.write_str(350, h)?;
            }
        }

        // ACAD_LAYOUT dictionary (handle 1A)
        w.write_str(0, DxfObjectCode::DICTIONARY)?;
        w.write_str(5, "1A")?;
        w.write_str(330, "C")?;
        w.write_str(100, SubclassMarker::DICTIONARY)?;
        w.write_i16(281, 1)?;
        for l in doc.layouts.iter() {
            if let Some(h) = l.handle() {
                w.write_str(3, l.name())?;
                w.write_str(350, h)?;
            }
        }

        // ACAD_MLINESTYLE dictionary (handle 17)
        w.write_str(0, DxfObjectCode::DICTIONARY)?;
        w.write_str(5, "17")?;
        w.write_str(330, "C")?;
        w.write_str(100, SubclassMarker::DICTIONARY)?;
        w.write_i16(281, 1)?;
        for ms in doc.mline_styles.iter() {
            if let Some(h) = ms.handle() {
                w.write_str(3, ms.name())?;
                w.write_str(350, h)?;
            }
        }

        // Groups
        for g in doc.groups.iter() {
            w.write_str(0, DxfObjectCode::GROUP)?;
            if let Some(h) = g.handle() { w.write_str(5, h)?; }
            w.write_str(330, "D")?;
            w.write_str(100, SubclassMarker::GROUP)?;
            w.write_str(300, &g.description)?;
            w.write_i16(70, i16::from(g.is_unnamed))?;
            w.write_i16(71, i16::from(g.is_selectable))?;
            for eh in &g.entity_handles {
                w.write_str(340, eh)?;
            }
        }

        // MLineStyles
        for ms in doc.mline_styles.iter() {
            w.write_str(0, DxfObjectCode::MLINE_STYLE)?;
            if let Some(h) = ms.handle() { w.write_str(5, h)?; }
            w.write_str(330, "17")?;
            w.write_str(100, SubclassMarker::MLINE_STYLE)?;
            w.write_str(2, ms.name())?;
            w.write_i16(70, ms.flags.bits())?;
            w.write_str(3, &ms.description)?;
            w.write_i16(62, ms.fill_color.index)?;
            w.write_f64(51, ms.start_angle)?;
            w.write_f64(52, ms.end_angle)?;
            w.write_i16(71, ms.elements.len() as i16)?;
            for el in &ms.elements {
                w.write_f64(49, el.offset)?;
                w.write_i16(62, el.color.index)?;
                w.write_str(6, &el.linetype)?;
            }
        }

        // RasterVariables
        w.write_str(0, DxfObjectCode::RASTER_VARIABLES)?;
        if let Some(h) = doc.raster_variables.object.handle.as_deref() {
            w.write_str(5, h)?;
        }
        w.write_str(100, SubclassMarker::RASTER_VARIABLES)?;
        w.write_i32(90, 0)?;
        w.write_i16(70, i16::from(doc.raster_variables.display_frame))?;
        w.write_i16(71, doc.raster_variables.display_quality as i16)?;
        w.write_i16(72, doc.raster_variables.units as i16)?;

        // MLeaderStyles
        for ms in doc.mleader_styles.iter() {
            w.write_str(0, "MLEADERSTYLE")?;
            if let Some(h) = ms.handle() { w.write_str(5, h)?; }
            w.write_str(100, "AcDbMLeaderStyle")?;
            w.write_str(300, ms.name())?;
            w.write_i16(170, ms.content_type as i16)?;
            w.write_i16(173, ms.leader_line_type)?;
            w.write_bool(290, ms.enable_landing)?;
            w.write_f64(42, ms.landing_gap)?;
            w.write_bool(291, ms.enable_dogleg)?;
            w.write_f64(43, ms.dogleg_length)?;
            w.write_f64(44, ms.arrow_size)?;
            w.write_f64(45, ms.text_height)?;
            w.write_f64(142, ms.scale)?;
        }

        // TableStyles
        for ts in doc.table_styles.iter() {
            w.write_str(0, "TABLESTYLE")?;
            if let Some(h) = ts.handle() { w.write_str(5, h)?; }
            w.write_str(100, "AcDbTableStyle")?;
            w.write_str(3, ts.name())?;
            w.write_i16(70, ts.flow_direction)?;
            w.write_f64(40, ts.horizontal_cell_margin)?;
            w.write_f64(41, ts.vertical_cell_margin)?;
            w.write_i16(280, i16::from(ts.suppress_title))?;
            w.write_i16(281, i16::from(ts.suppress_header))?;
        }

        // GeoData
        if let Some(geo) = &doc.geo_data {
            w.write_str(0, "GEODATA")?;
            if let Some(h) = geo.object.handle.as_deref() { w.write_str(5, h)?; }
            w.write_str(100, "AcDbGeoData")?;
            w.write_i32(90, geo.version)?;
            if let Some(hb) = &geo.host_block_handle { w.write_str(330, hb)?; }
            w.write_i16(70, geo.design_coordinates_type)?;
            w.write_f64(10, geo.design_point.x)?;
            w.write_f64(20, geo.design_point.y)?;
            w.write_f64(30, geo.design_point.z)?;
            w.write_f64(11, geo.reference_point.x)?;
            w.write_f64(21, geo.reference_point.y)?;
            w.write_f64(31, geo.reference_point.z)?;
            w.write_f64(40, geo.horizontal_unit_scale)?;
            w.write_i32(91, geo.horizontal_units)?;
            w.write_f64(41, geo.vertical_unit_scale)?;
            w.write_i32(92, geo.vertical_units)?;
            w.write_f64(210, geo.up_direction.x)?;
            w.write_f64(220, geo.up_direction.y)?;
            w.write_f64(230, geo.up_direction.z)?;
            w.write_f64(12, geo.north_direction.x)?;
            w.write_f64(22, geo.north_direction.y)?;
            w.write_i32(95, geo.scale_estimation_method)?;
            w.write_f64(141, geo.user_scale_factor)?;
            if !geo.coordinate_system_definition.is_empty() {
                w.write_str(301, &geo.coordinate_system_definition)?;
            }
            if !geo.geo_rss_tag.is_empty() {
                w.write_str(302, &geo.geo_rss_tag)?;
            }
        }

        // Sun
        if let Some(sun) = &doc.sun {
            w.write_str(0, "SUN")?;
            if let Some(h) = sun.object.handle.as_deref() { w.write_str(5, h)?; }
            w.write_str(100, "AcDbSun")?;
            w.write_i32(90, sun.version)?;
            w.write_bool(290, sun.status)?;
            w.write_i16(63, sun.color.index)?;
            w.write_f64(40, sun.intensity)?;
            w.write_bool(291, sun.shadows)?;
            w.write_i32(91, sun.julian_day)?;
            w.write_i32(92, sun.time_milliseconds)?;
            w.write_bool(292, sun.daylight_savings)?;
            w.write_i16(70, sun.shadow_type as i16)?;
            w.write_i16(71, sun.shadow_map_size)?;
            w.write_i16(280, sun.shadow_softness)?;
        }

        // SunStudies
        for ss in &doc.sun_studies {
            w.write_str(0, "SUNSTUDY")?;
            if let Some(h) = ss.object.handle.as_deref() { w.write_str(5, h)?; }
            w.write_str(100, "AcDbSunStudy")?;
            w.write_i32(90, ss.version)?;
            w.write_str(1, &ss.name)?;
            w.write_str(2, &ss.description)?;
            w.write_i16(70, ss.output_type)?;
            w.write_str(3, &ss.sheet_set_name)?;
            w.write_bool(290, ss.use_subset)?;
            w.write_str(4, &ss.sheet_subset_name)?;
            w.write_bool(291, ss.select_dates_from_calendar)?;
            for &(day, secs) in &ss.julian_dates {
                w.write_i32(91, day)?;
                w.write_i32(91, secs)?;
            }
            w.write_i32(93, ss.start_time_seconds)?;
            w.write_i32(94, ss.end_time_seconds)?;
            w.write_i32(95, ss.interval_seconds)?;
            w.write_bool(293, ss.lock_view_viewport)?;
            w.write_i16(74, ss.shade_plot_type)?;
            w.write_i16(75, ss.viewports_per_page as i16)?;
            w.write_f64(40, ss.spacing_between_viewports)?;
        }

        Self::end_section(w)
    }

    fn write_thumbnail(doc: &DxfDocument, w: &mut dyn ICodeValueWriter) -> Result<()> {
        Self::begin_section(w, DxfObjectCode::THUMBNAIL_IMAGE_SECTION)?;
        w.write_i32(90, doc.thumbnail_image.len() as i32)?;
        for chunk in doc.thumbnail_image.chunks(127) {
            w.write_bytes(310, chunk)?;
        }
        Self::end_section(w)
    }
}
