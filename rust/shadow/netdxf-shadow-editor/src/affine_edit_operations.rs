use crate::DimensionTranslation;
use netdxf_shadow::{
    Affine3, ObjectId, ShadowEditBatch, ShadowPropertyEdit, ShadowReader, ShadowSnapshot,
    ShadowValue, Vec3,
};
use serde::{Deserialize, Serialize};
use std::collections::BTreeSet;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct AffineEditOptions {
    pub preserve_non_uniform_scale: bool,
    pub tolerance: f64,
    pub relative_tolerance: f64,
    pub maximum_coordinates: usize,
    pub maximum_entities: usize,
}

impl Default for AffineEditOptions {
    fn default() -> Self {
        Self {
            preserve_non_uniform_scale: true,
            tolerance: 1e-9,
            relative_tolerance: 1e-6,
            maximum_coordinates: 1_000_000,
            maximum_entities: 100_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AffineEntityEdit {
    pub entity: ObjectId,
    pub transform: Affine3,
}

/// Representation-preserving affine edit planner over detached [`ShadowSnapshot`] data
/// (`AffineEditOperations.cs`, `AffineEditOperations.Planar.cs`, `AffineEditOperations.Hatch.cs`).
pub struct AffineEditOperations;

impl AffineEditOperations {
    /// Builds a property edit batch applying each [`AffineEntityEdit`] with full per-entity
    /// geometric and OCS semantics (Line, Point, Face3D, Circle, Arc, Ellipse via 2x2 SVD,
    /// Polyline2D/3D, Spline, Solid/Trace, Image, Text/Attribute/MText, Insert+Attributes, Hatch,
    /// and Dimensions).
    pub fn transform_entities(
        snapshot: &ShadowSnapshot,
        edits: &[AffineEntityEdit],
        options: &AffineEditOptions,
    ) -> ShadowEditBatch {
        let mut planner = AffinePlanner {
            snapshot,
            options,
            visited: BTreeSet::new(),
            prop_edits: Vec::new(),
            coordinates: 0,
        };
        for edit in edits {
            if planner.visited.len() >= options.maximum_entities {
                break;
            }
            planner.visit(&edit.entity, edit.transform);
        }
        ShadowEditBatch::create(snapshot, planner.prop_edits)
    }

    pub fn plan(
        snapshot: &ShadowSnapshot,
        edits: &[AffineEntityEdit],
        options: &AffineEditOptions,
    ) -> ShadowEditBatch {
        Self::transform_entities(snapshot, edits, options)
    }

    pub fn plan_selection(
        snapshot: &ShadowSnapshot,
        entities: &[ObjectId],
        transform: Affine3,
        options: &AffineEditOptions,
    ) -> ShadowEditBatch {
        let edits: Vec<AffineEntityEdit> = entities
            .iter()
            .cloned()
            .map(|entity| AffineEntityEdit { entity, transform })
            .collect();
        Self::transform_entities(snapshot, &edits, options)
    }
}

struct PlaneData {
    local: Affine3,
    normal: Vec3,
    scale_x: f64,
    scale_y: f64,
}

struct AffinePlanner<'a> {
    snapshot: &'a ShadowSnapshot,
    options: &'a AffineEditOptions,
    visited: BTreeSet<ObjectId>,
    prop_edits: Vec<ShadowPropertyEdit>,
    coordinates: usize,
}

impl<'a> AffinePlanner<'a> {
    fn visit(&mut self, id: &ObjectId, transform: Affine3) {
        if !self.visited.insert(id.clone()) || transform == Affine3::IDENTITY {
            return;
        }
        let entity = self.snapshot.read(id.clone());
        if !entity.exists() {
            return;
        }

        if DimensionTranslation::is_dimension_type(entity.type_name())
            && is_pure_translation(&transform)
        {
            DimensionTranslation::translate(&entity, transform.translation, &mut self.prop_edits);
            return;
        }

        match entity.type_name() {
            "Point" => {
                self.point(&entity, "Position", transform, false);
                self.extrusion(&entity, transform);
            }
            "Line" => {
                self.point(&entity, "StartPoint", transform, false);
                self.point(&entity, "EndPoint", transform, false);
                self.extrusion(&entity, transform);
            }
            "Face3D" => {
                for name in ["FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex"] {
                    self.point(&entity, name, transform, false);
                }
            }
            "Polyline3D" | "Mesh" | "PolyfaceMesh" | "Leader" => {
                self.sequence(&entity, "Vertexes", transform);
            }
            "Spline" => {
                self.sequence(&entity, "ControlPoints", transform);
                if !entity.field("FitPoints").items().is_empty() {
                    self.sequence(&entity, "FitPoints", transform);
                }
                for name in ["StartTangent", "EndTangent"] {
                    if entity.field(name).exists() {
                        self.point(&entity, name, transform, true);
                    }
                }
            }
            "Circle" | "Arc" => self.conic(&entity, transform),
            "Ellipse" => self.ellipse(&entity, transform),
            "Polyline2D" => self.polyline2d(&entity, transform),
            "Solid" | "Trace" => {
                if let Some(plane) = self.plane(&entity, transform) {
                    let elev = entity.field("Elevation").number(0.0);
                    for name in ["FirstVertex", "SecondVertex", "ThirdVertex", "FourthVertex"] {
                        let p = entity.field(name).vector(Vec3::ZERO);
                        let local_p = plane.local.point(Vec3::new(p.x, p.y, elev));
                        self.set_vector(&entity, name, local_p);
                    }
                    self.set_plane(&entity, &plane);
                    self.thickness(&entity, transform, plane.normal);
                }
            }
            "Image" => self.image(&entity, transform),
            "Text" | "Attribute" | "AttributeDefinition" => self.text(&entity, transform),
            "MText" => self.mtext(&entity, transform),
            "Insert" => self.insert(&entity, transform),
            "Hatch" => self.hatch(&entity, transform),
            _ => {
                for prop in ["StartPoint", "EndPoint", "Center", "Position"] {
                    self.point(&entity, prop, transform, false);
                }
            }
        }
    }

    fn set_value(&mut self, entity: &ShadowReader<'_>, name: &str, value: ShadowValue) {
        let Some(id) = entity.id().cloned() else {
            return;
        };
        let before = entity.field(name).value.clone();
        if before != value {
            self.prop_edits.push(ShadowPropertyEdit {
                object: id,
                property: name.to_string(),
                before,
                after: value,
            });
        }
    }

    fn set_number(&mut self, entity: &ShadowReader<'_>, name: &str, value: f64) {
        if value.is_finite() {
            self.set_value(entity, name, ShadowValue::from_f64(value));
        }
    }

    fn set_vector(&mut self, entity: &ShadowReader<'_>, name: &str, value: Vec3) {
        if !value.is_finite() || self.coordinates >= self.options.maximum_coordinates {
            return;
        }
        let field = entity.field(name);
        if !field.exists() {
            return;
        }
        self.coordinates += 1;
        let has_z = matches!(&field.value, ShadowValue::Structure(s) if s.fields.contains_key("Z"));
        self.set_value(entity, name, ShadowValue::from_vec3(value, has_z));
    }

    fn point(&mut self, entity: &ShadowReader<'_>, name: &str, transform: Affine3, direction: bool) {
        let field = entity.field(name);
        if !field.exists() {
            return;
        }
        let v = field.vector(Vec3::ZERO);
        let mapped = if direction {
            transform.direction(v)
        } else {
            transform.point(v)
        };
        self.set_vector(entity, name, mapped);
    }

    fn sequence(&mut self, entity: &ShadowReader<'_>, name: &str, transform: Affine3) {
        let Some(id) = entity.id().cloned() else {
            return;
        };
        let field = entity.field(name);
        let items = field.items();
        if items.is_empty() {
            return;
        }
        let mut has_inline = false;
        let mut values = Vec::with_capacity(items.len());
        for item in &items {
            if item.id().is_some() {
                self.point(item, "Position", transform, false);
                values.push(item.value.clone());
            } else {
                has_inline = true;
                let mapped = transform.point(item.vector(Vec3::ZERO));
                let has_z = matches!(&item.value, ShadowValue::Structure(s) if s.fields.contains_key("Z"));
                values.push(ShadowValue::from_vec3(mapped, has_z));
            }
        }
        if has_inline {
            self.prop_edits.push(ShadowPropertyEdit {
                object: id,
                property: name.to_string(),
                before: field.value.clone(),
                after: ShadowValue::Sequence(netdxf_shadow::Sequence { items: values }),
            });
        }
    }

    fn extrusion(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        if !entity.field("Normal").exists() {
            return;
        }
        let old_normal = entity.field("Normal").vector(Vec3::UNIT_Z);
        let transformed = transform.direction(old_normal);
        let len = transformed.length();
        if len > 0.0 && len.is_finite() {
            self.set_vector(entity, "Normal", transformed * (1.0 / len));
            if entity.field("Thickness").exists() {
                let thick = entity.field("Thickness").number(0.0);
                self.set_number(entity, "Thickness", thick * len);
            }
        }
    }

    fn thickness(&mut self, entity: &ShadowReader<'_>, transform: Affine3, normal: Vec3) {
        let thick = entity.field("Thickness").number(0.0);
        if thick == 0.0 {
            return;
        }
        let dir = transform.direction(entity.field("Normal").vector(Vec3::UNIT_Z));
        let scale = Vec3::dot(dir, normal);
        self.set_number(entity, "Thickness", thick * scale);
    }

    fn plane(&self, entity: &ShadowReader<'_>, transform: Affine3) -> Option<PlaneData> {
        let old_n = entity.field("Normal").vector(Vec3::UNIT_Z);
        let old = Affine3::object_coordinates(if old_n.length() > 0.0 {
            old_n
        } else {
            Vec3::UNIT_Z
        });
        let u = transform.direction(old.x);
        let v = transform.direction(old.y);
        let sx = u.length();
        let sy = v.length();
        if sx <= 0.0 || sy <= 0.0 || !(sx + sy).is_finite() {
            return None;
        }
        let normal = Vec3::cross(u * (1.0 / sx), v * (1.0 / sy)).normalized();
        let basis = Affine3::object_coordinates(normal);
        let inv = basis.try_inverse(self.options.relative_tolerance)?;
        let local = inv * transform * old;
        Some(PlaneData {
            local,
            normal,
            scale_x: sx,
            scale_y: sy,
        })
    }

    fn set_plane(&mut self, entity: &ShadowReader<'_>, plane: &PlaneData) {
        self.set_vector(entity, "Normal", plane.normal);
        let elev = entity.field("Elevation").number(0.0);
        let new_elev = plane.local.point(Vec3::new(0.0, 0.0, elev)).z;
        if entity.field("Elevation").exists() || new_elev.abs() > self.options.tolerance {
            self.set_number(entity, "Elevation", new_elev);
        }
    }

    fn conic(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let Some(plane) = self.plane(entity, transform) else {
            return;
        };
        self.point(entity, "Center", transform, false);
        let radius = entity.field("Radius").number(0.0);
        self.set_number(entity, "Radius", radius * 0.5 * (plane.scale_x + plane.scale_y));
        self.set_vector(entity, "Normal", plane.normal);
        self.thickness(entity, transform, plane.normal);
        if entity.type_name() == "Arc" {
            for name in ["StartAngle", "EndAngle"] {
                let rad = entity.field(name).number(0.0).to_radians();
                let d = plane.local.direction(Vec3::new(rad.cos(), rad.sin(), 0.0));
                self.set_number(entity, name, degrees_of(d));
            }
        }
    }

    fn ellipse(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let Some(plane) = self.plane(entity, transform) else {
            return;
        };
        let rot = Affine3::rotate_z(entity.field("Rotation").number(0.0).to_radians());
        let maj_old = entity.field("MajorAxis").number(2.0);
        let min_old = entity.field("MinorAxis").number(1.0);
        let u = plane.local.direction(rot.x) * (maj_old * 0.5);
        let v = plane.local.direction(rot.y) * (min_old * 0.5);
        let scale = u.length().max(v.length());
        if scale <= 0.0 || !scale.is_finite() {
            return;
        }
        let a = u * (1.0 / scale);
        let b = v * (1.0 / scale);
        let xx = a.x * a.x + b.x * b.x;
        let yy = a.y * a.y + b.y * b.y;
        let xy = a.x * a.y + b.x * b.y;
        let angle = 0.5 * (2.0 * xy).atan2(xx - yy);
        let x = Vec3::new(angle.cos(), angle.sin(), 0.0);
        let y = Vec3::new(-x.y, x.x, 0.0);
        let disc = ((xx - yy) * (xx - yy) + 4.0 * xy * xy).sqrt();
        let major = ((xx + yy + disc) * 0.5).max(0.0).sqrt();
        let minor = if major > 0.0 {
            (a.x * b.y - a.y * b.x).abs() / major
        } else {
            0.0
        };
        if minor <= 0.0 || !minor.is_finite() {
            return;
        }

        self.point(entity, "Center", transform, false);
        self.set_vector(entity, "Normal", plane.normal);
        self.set_number(entity, "MajorAxis", 2.0 * scale * major);
        self.set_number(entity, "MinorAxis", 2.0 * scale * minor);
        self.set_number(entity, "Rotation", angle.to_degrees());
        self.thickness(entity, transform, plane.normal);

        if entity.field("IsFullEllipse").boolean(false) {
            self.set_number(entity, "StartAngle", 0.0);
            self.set_number(entity, "EndAngle", 0.0);
            return;
        }
        for name in ["StartAngle", "EndAngle"] {
            let polar = entity.field(name).number(0.0).to_radians();
            let param = (maj_old * polar.sin()).atan2(min_old * polar.cos());
            let pt = u * param.cos() + v * param.sin();
            let deg = Vec3::dot(pt, y).atan2(Vec3::dot(pt, x)).to_degrees();
            self.set_number(entity, name, deg);
        }
    }

    fn polyline2d(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let Some(plane) = self.plane(entity, transform) else {
            return;
        };
        let elev = entity.field("Elevation").number(0.0);
        for vertex in entity.field("Vertexes").items() {
            let p = vertex.field("Position").vector(Vec3::ZERO);
            let mapped = plane.local.point(Vec3::new(p.x, p.y, elev));
            self.set_vector(&vertex, "Position", mapped);
            for w in ["StartWidth", "EndWidth"] {
                let val = vertex.field(w).number(0.0);
                if val != 0.0 {
                    self.set_number(&vertex, w, val * plane.scale_x);
                }
            }
        }
        self.set_plane(entity, &plane);
        self.thickness(entity, transform, plane.normal);
    }

    fn image(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let Some(plane) = self.plane(entity, transform) else {
            return;
        };
        let u = plane
            .local
            .direction(entity.field("Uvector").vector(Vec3::UNIT_X));
        let v = plane
            .local
            .direction(entity.field("Vvector").vector(Vec3::UNIT_Y));
        let ulen = u.length();
        let vlen = v.length();
        self.point(entity, "Position", transform, false);
        self.set_vector(entity, "Normal", plane.normal);
        if ulen > 0.0 {
            self.set_vector(entity, "Uvector", u * (1.0 / ulen));
            self.set_number(entity, "Width", entity.field("Width").number(0.0) * ulen);
        }
        if vlen > 0.0 {
            self.set_vector(entity, "Vvector", v * (1.0 / vlen));
            self.set_number(entity, "Height", entity.field("Height").number(0.0) * vlen);
        }
    }

    fn text(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let Some(plane) = self.plane(entity, transform) else {
            return;
        };
        let rot = Affine3::rotate_z(entity.field("Rotation").number(0.0).to_radians());
        let oblique = entity.field("ObliqueAngle").number(0.0).to_radians();
        let u = plane.local.direction(rot.x);
        let v = plane.local.direction(rot.y + rot.x * oblique.tan());
        let ulen = u.length();
        if ulen <= 0.0 || !ulen.is_finite() {
            return;
        }
        let x = u * (1.0 / ulen);
        let perp = Vec3::new(-x.y, x.x, 0.0);
        let height_scale = Vec3::dot(v, perp);
        if height_scale <= 0.0 || !height_scale.is_finite() {
            return;
        }
        self.point(entity, "Position", transform, false);
        self.set_vector(entity, "Normal", plane.normal);
        self.set_number(entity, "Rotation", degrees_of(x));
        self.set_number(
            entity,
            "Height",
            entity.field("Height").number(1.0) * height_scale,
        );
        self.set_number(
            entity,
            "WidthFactor",
            entity.field("WidthFactor").number(1.0) * ulen / height_scale,
        );
        self.set_number(
            entity,
            "ObliqueAngle",
            Vec3::dot(v, x).atan2(height_scale).to_degrees(),
        );
        self.thickness(entity, transform, plane.normal);
        if entity.field("ProxyGraphics").exists() {
            self.set_value(entity, "ProxyGraphics", ShadowValue::null());
        }
    }

    fn mtext(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let Some(plane) = self.plane(entity, transform) else {
            return;
        };
        self.point(entity, "Position", transform, false);
        self.set_vector(entity, "Normal", plane.normal);
        let dir = Affine3::rotate_z(entity.field("Rotation").number(0.0).to_radians()).x;
        self.set_number(entity, "Rotation", degrees_of(plane.local.direction(dir)));
        self.set_number(
            entity,
            "Height",
            entity.field("Height").number(1.0) * plane.scale_x,
        );
        if entity.field("RectangleWidth").exists() {
            self.set_number(
                entity,
                "RectangleWidth",
                entity.field("RectangleWidth").number(0.0) * plane.scale_x,
            );
        }
    }

    fn insert(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        let old_n = entity.field("Normal").vector(Vec3::UNIT_Z);
        let basis = Affine3::object_coordinates(if old_n.length() > 0.0 {
            old_n
        } else {
            Vec3::UNIT_Z
        }) * Affine3::rotate_z(entity.field("Rotation").number(0.0).to_radians());
        let scale = entity.field("Scale").vector(Vec3::new(1.0, 1.0, 1.0));
        let u = transform.direction(basis.x * scale.x);
        let v = transform.direction(basis.y * scale.y);
        let z = transform.direction(basis.z * scale.z);
        let sx = u.length();
        let sy = v.length();
        if !(sx > 0.0 && sy > 0.0) {
            return;
        }
        let normal = Vec3::cross(u * (1.0 / sx), v * (1.0 / sy)).normalized();
        let sz = Vec3::dot(z, normal);
        let target = Affine3::object_coordinates(normal);
        self.point(entity, "Position", transform, false);
        self.set_vector(entity, "Normal", normal);
        self.set_vector(entity, "Scale", Vec3::new(sx, sy, sz));
        let rot = Vec3::dot(u, target.y).atan2(Vec3::dot(u, target.x)).to_degrees();
        self.set_number(entity, "Rotation", rot);
        for attr in entity.field("Attributes").items() {
            if let Some(aid) = attr.id() {
                self.visit(aid, transform);
            }
        }
    }

    fn hatch(&mut self, entity: &ShadowReader<'_>, transform: Affine3) {
        if !is_pure_translation(&transform) || entity.field("Associative").boolean(false) {
            return;
        }
        let normal = entity.field("Normal").vector(Vec3::UNIT_Z);
        let basis = Affine3::object_coordinates(if normal.length() > 0.0 {
            normal
        } else {
            Vec3::UNIT_Z
        });
        let m = transform.translation;
        let local = Vec3::new(Vec3::dot(m, basis.x), Vec3::dot(m, basis.y), Vec3::dot(m, basis.z));
        if !local.is_finite() {
            return;
        }
        let shift_xy = Affine3::move_by(Vec3::new(local.x, local.y, 0.0));
        for path in entity.field("BoundaryPaths").items() {
            for edge in path.field("Edges").items() {
                match edge.type_name() {
                    "HatchBoundaryPath+Line" => {
                        self.point(&edge, "Start", shift_xy, false);
                        self.point(&edge, "End", shift_xy, false);
                    }
                    "HatchBoundaryPath+Arc" | "HatchBoundaryPath+Ellipse" => {
                        self.point(&edge, "Center", shift_xy, false);
                    }
                    "HatchBoundaryPath+Polyline" => {
                        self.sequence(&edge, "Vertexes", shift_xy);
                    }
                    "HatchBoundaryPath+Spline" => {
                        self.sequence(&edge, "ControlPoints", shift_xy);
                        if edge.field("FitPoints").exists() {
                            self.sequence(&edge, "FitPoints", shift_xy);
                        }
                    }
                    _ => {}
                }
            }
        }
        if entity.field("SeedPoints").exists() {
            self.sequence(entity, "SeedPoints", shift_xy);
        }
        if local.z.abs() > 0.0 {
            let elev = entity.field("Elevation").number(0.0);
            self.set_number(entity, "Elevation", elev + local.z);
        }
    }
}

fn is_pure_translation(t: &Affine3) -> bool {
    t.x == Vec3::UNIT_X && t.y == Vec3::UNIT_Y && t.z == Vec3::UNIT_Z
}

fn degrees_of(d: Vec3) -> f64 {
    let mut deg = d.y.atan2(d.x).to_degrees();
    if deg < 0.0 {
        deg += 360.0;
    }
    deg
}
