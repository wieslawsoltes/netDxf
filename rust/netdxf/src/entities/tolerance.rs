use crate::dxf_object_code::DxfObjectCode;
use crate::entities::entity_object::EntityCommon;
use crate::entities::entity_type::EntityType;
use crate::entities::{
    DatumReferenceValue, ToleranceEntry, ToleranceGeometricSymbol, ToleranceMaterialCondition,
    ToleranceValue,
};
use crate::tables::DimensionStyle;
use crate::vector3::Vector3;

/// Represents a DXF `TOLERANCE` (geometric tolerance / feature control frame) entity.
#[derive(Clone, Debug, PartialEq)]
pub struct Tolerance {
    pub common: EntityCommon,
    pub style: String,
    pub position: Vector3,
    pub rotation: f64,
    pub raw_string: String,
    pub entry1: Option<ToleranceEntry>,
    pub entry2: Option<ToleranceEntry>,
    pub projected_tolerance_zone_value: String,
    pub show_projected_tolerance_zone_symbol: bool,
    pub datum_identifier: String,
    pub text_height: f64,
}

impl Tolerance {
    pub fn new(raw_string: impl Into<String>, position: Vector3) -> Self {
        let raw = raw_string.into();
        let mut tol = Self {
            common: EntityCommon::new(EntityType::Tolerance, DxfObjectCode::TOLERANCE),
            style: DimensionStyle::DEFAULT_NAME.to_string(),
            position,
            rotation: 0.0,
            raw_string: raw.clone(),
            entry1: None,
            entry2: None,
            projected_tolerance_zone_value: String::new(),
            show_projected_tolerance_zone_symbol: false,
            datum_identifier: String::new(),
            text_height: 0.0,
        };
        if !raw.is_empty() {
            tol.parse_string_representation(&raw);
        }
        tol
    }

    pub fn from_entry(entry1: ToleranceEntry, position: Vector3) -> Self {
        let mut tol = Self::new(String::new(), position);
        tol.entry1 = Some(entry1);
        tol.raw_string = tol.to_string_representation();
        tol
    }

    /// Formats the structured tolerance entries into the AutoCAD DXF `TOLERANCE` string representation.
    pub fn to_string_representation(&self) -> String {
        if self.entry1.is_none()
            && self.entry2.is_none()
            && self.projected_tolerance_zone_value.is_empty()
            && self.datum_identifier.is_empty()
            && !self.raw_string.is_empty()
        {
            return self.raw_string.clone();
        }
        let mut lines = Vec::new();
        if let Some(e1) = &self.entry1 {
            lines.push(format_entry(e1));
        }
        if let Some(e2) = &self.entry2 {
            lines.push(format_entry(e2));
        }
        if !self.projected_tolerance_zone_value.is_empty()
            || self.show_projected_tolerance_zone_symbol
        {
            let mut proj = self.projected_tolerance_zone_value.clone();
            if self.show_projected_tolerance_zone_symbol {
                proj.push_str("{\\Fgdt;p}");
            }
            lines.push(proj);
        }
        if !self.datum_identifier.is_empty() {
            lines.push(format!("%%v{}%%v", self.datum_identifier));
        }
        lines.join("^J")
    }

    pub fn try_parse_string_representation(value: &str) -> Option<Self> {
        if value.trim().is_empty() {
            return None;
        }
        Some(Self::new(value, Vector3::ZERO))
    }

    pub fn transform_by(&mut self, transformation: crate::Matrix3, translation: Vector3) {
        self.position = transformation * self.position + translation;
        if let Some(n) = Vector3::normalize_finite_direction(transformation * self.common.normal) {
            self.common.normal = n;
        }
    }

    /// Parses an AutoCAD DXF `TOLERANCE` string (`%%v` frames separated by `^J`) into structured fields.
    pub fn parse_string_representation(&mut self, value: &str) {
        self.raw_string = value.to_string();
        self.entry1 = None;
        self.entry2 = None;
        self.projected_tolerance_zone_value.clear();
        self.show_projected_tolerance_zone_symbol = false;
        self.datum_identifier.clear();

        let lines: Vec<&str> = value.split("^J").collect();
        for (idx, line) in lines.iter().enumerate() {
            if line.contains("{\\Fgdt;p}") {
                self.show_projected_tolerance_zone_symbol = true;
                self.projected_tolerance_zone_value = line.replace("{\\Fgdt;p}", "").trim().to_string();
            } else if line.contains("%%v") {
                let cells: Vec<&str> = line.split("%%v").collect();
                if cells.len() == 3 && cells[0].is_empty() && cells[2].is_empty() {
                    self.datum_identifier = cells[1].to_string();
                } else {
                    let parsed = parse_entry(&cells);
                    if idx == 0 && self.entry1.is_none() {
                        self.entry1 = Some(parsed);
                    } else if self.entry2.is_none() {
                        self.entry2 = Some(parsed);
                    }
                }
            }
        }
    }
}

fn symbol_to_gdt(sym: ToleranceGeometricSymbol) -> &'static str {
    match sym {
        ToleranceGeometricSymbol::None => "",
        ToleranceGeometricSymbol::Position => "{\\Fgdt;j}",
        ToleranceGeometricSymbol::Concentricity => "{\\Fgdt;r}",
        ToleranceGeometricSymbol::Symmetry => "{\\Fgdt;i}",
        ToleranceGeometricSymbol::Parallelism => "{\\Fgdt;f}",
        ToleranceGeometricSymbol::Perpendicularity => "{\\Fgdt;b}",
        ToleranceGeometricSymbol::Angularity => "{\\Fgdt;a}",
        ToleranceGeometricSymbol::Cylindricity => "{\\Fgdt;g}",
        ToleranceGeometricSymbol::Flatness => "{\\Fgdt;c}",
        ToleranceGeometricSymbol::Roundness => "{\\Fgdt;e}",
        ToleranceGeometricSymbol::Straightness => "{\\Fgdt;u}",
        ToleranceGeometricSymbol::ProfileSurface => "{\\Fgdt;d}",
        ToleranceGeometricSymbol::ProfileLine => "{\\Fgdt;k}",
        ToleranceGeometricSymbol::CircularRunout => "{\\Fgdt;h}",
        ToleranceGeometricSymbol::TotalRunOut => "{\\Fgdt;t}",
    }
}

fn gdt_to_symbol(cell: &str) -> ToleranceGeometricSymbol {
    match cell.trim() {
        "{\\Fgdt;j}" => ToleranceGeometricSymbol::Position,
        "{\\Fgdt;r}" => ToleranceGeometricSymbol::Concentricity,
        "{\\Fgdt;i}" => ToleranceGeometricSymbol::Symmetry,
        "{\\Fgdt;f}" => ToleranceGeometricSymbol::Parallelism,
        "{\\Fgdt;b}" => ToleranceGeometricSymbol::Perpendicularity,
        "{\\Fgdt;a}" => ToleranceGeometricSymbol::Angularity,
        "{\\Fgdt;g}" => ToleranceGeometricSymbol::Cylindricity,
        "{\\Fgdt;c}" => ToleranceGeometricSymbol::Flatness,
        "{\\Fgdt;e}" => ToleranceGeometricSymbol::Roundness,
        "{\\Fgdt;u}" => ToleranceGeometricSymbol::Straightness,
        "{\\Fgdt;d}" => ToleranceGeometricSymbol::ProfileSurface,
        "{\\Fgdt;k}" => ToleranceGeometricSymbol::ProfileLine,
        "{\\Fgdt;h}" => ToleranceGeometricSymbol::CircularRunout,
        "{\\Fgdt;t}" => ToleranceGeometricSymbol::TotalRunOut,
        _ => ToleranceGeometricSymbol::None,
    }
}

fn material_to_gdt(mc: ToleranceMaterialCondition) -> &'static str {
    match mc {
        ToleranceMaterialCondition::None => "",
        ToleranceMaterialCondition::Maximum => "{\\Fgdt;m}",
        ToleranceMaterialCondition::Least => "{\\Fgdt;l}",
        ToleranceMaterialCondition::Regardless => "{\\Fgdt;s}",
    }
}

fn parse_material(cell: &str) -> (String, ToleranceMaterialCondition) {
    if cell.contains("{\\Fgdt;m}") {
        (cell.replace("{\\Fgdt;m}", ""), ToleranceMaterialCondition::Maximum)
    } else if cell.contains("{\\Fgdt;l}") {
        (cell.replace("{\\Fgdt;l}", ""), ToleranceMaterialCondition::Least)
    } else if cell.contains("{\\Fgdt;s}") {
        (cell.replace("{\\Fgdt;s}", ""), ToleranceMaterialCondition::Regardless)
    } else {
        (cell.to_string(), ToleranceMaterialCondition::None)
    }
}

fn format_tol_val(tv: &ToleranceValue) -> String {
    let mut s = String::new();
    if tv.show_diameter_symbol {
        s.push_str("{\\Fgdt;n}");
    }
    s.push_str(&tv.value);
    s.push_str(material_to_gdt(tv.material_condition));
    s
}

fn format_datum(d: &DatumReferenceValue) -> String {
    format!("{}{}", d.value, material_to_gdt(d.material_condition))
}

fn format_entry(entry: &ToleranceEntry) -> String {
    let mut cells = vec![
        symbol_to_gdt(entry.geometric_symbol).to_string(),
        entry.tolerance1.as_ref().map_or_else(String::new, format_tol_val),
        entry.tolerance2.as_ref().map_or_else(String::new, format_tol_val),
        entry.datum1.as_ref().map_or_else(String::new, format_datum),
        entry.datum2.as_ref().map_or_else(String::new, format_datum),
        entry.datum3.as_ref().map_or_else(String::new, format_datum),
    ];
    while cells.len() > 1 && cells.last().is_some_and(|c| c.is_empty()) {
        cells.pop();
    }
    cells.join("%%v")
}

fn parse_entry(cells: &[&str]) -> ToleranceEntry {
    let mut entry = ToleranceEntry::new();
    if let Some(&first) = cells.first() {
        entry.geometric_symbol = gdt_to_symbol(first);
    }
    if let Some(&c1) = cells.get(1) {
        if !c1.is_empty() {
            let show_dia = c1.contains("{\\Fgdt;n}");
            let stripped = c1.replace("{\\Fgdt;n}", "");
            let (val, mc) = parse_material(&stripped);
            entry.tolerance1 = Some(ToleranceValue {
                show_diameter_symbol: show_dia,
                value: val,
                material_condition: mc,
            });
        }
    }
    if let Some(&c2) = cells.get(2) {
        if !c2.is_empty() {
            let show_dia = c2.contains("{\\Fgdt;n}");
            let stripped = c2.replace("{\\Fgdt;n}", "");
            let (val, mc) = parse_material(&stripped);
            entry.tolerance2 = Some(ToleranceValue {
                show_diameter_symbol: show_dia,
                value: val,
                material_condition: mc,
            });
        }
    }
    for (i, slot) in [&mut entry.datum1, &mut entry.datum2, &mut entry.datum3]
        .into_iter()
        .enumerate()
    {
        if let Some(&dc) = cells.get(3 + i) {
            if !dc.is_empty() {
                let (val, mc) = parse_material(dc);
                *slot = Some(DatumReferenceValue {
                    value: val,
                    material_condition: mc,
                });
            }
        }
    }
    entry
}
