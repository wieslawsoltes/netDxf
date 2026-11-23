use netdxf_shadow::{
    Affine3, DiagnosticSeverity, ObjectId, ShadowObjectKind, ShadowReader, ShadowSnapshot,
    ShadowValue, Vec3,
};
use netdxf_shadow_geometry::CadScene;
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet, HashMap, HashSet};

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DrawingHealthOptions {
    pub tiny_length: f64,
    pub gap_tolerance: f64,
    pub maximum_issues: usize,
    pub maximum_work: i64,
    pub maximum_objects: usize,
    pub maximum_value_depth: usize,
    pub find_duplicates: bool,
    pub find_tiny_gaps: bool,
    pub find_unused_resources: bool,
}

impl Default for DrawingHealthOptions {
    fn default() -> Self {
        Self {
            tiny_length: 1e-7,
            gap_tolerance: 0.01,
            maximum_issues: 10_000,
            maximum_work: 5_000_000,
            maximum_objects: 250_000,
            maximum_value_depth: 64,
            find_duplicates: true,
            find_tiny_gaps: true,
            find_unused_resources: true,
        }
    }
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingHealthResources {
    pub available_shx_fonts: BTreeSet<String>,
    pub available_images: BTreeSet<String>,
    pub presentation_diagnostics: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DrawingHealthIssue {
    pub id: String,
    pub code: String,
    pub severity: DiagnosticSeverity,
    pub object: Option<ObjectId>,
    pub path: Option<String>,
    pub position: Option<Vec3>,
    pub related_objects: Vec<ObjectId>,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DrawingHealthReport {
    pub document_id: String,
    pub revision: i64,
    pub complete: bool,
    pub work: i64,
    pub coverage: Option<String>,
    pub limitations: Vec<String>,
    pub issues: Vec<DrawingHealthIssue>,
}

/// Evaluates geometry, topology gap, resource, and block-recursion health checks over a
/// [`ShadowSnapshot`] and [`CadScene`].
pub struct DrawingHealth;

impl DrawingHealth {
    pub fn analyze(
        snapshot: &ShadowSnapshot,
        scene: &CadScene,
        options: &DrawingHealthOptions,
        resources: &DrawingHealthResources,
    ) -> DrawingHealthReport {
        let mut analyzer = Analyzer::new(snapshot, scene, options, resources);
        analyzer.run()
    }
}

struct EndpointEntry {
    entity: ObjectId,
    owner: ObjectId,
    point: Vec3,
}

struct Analyzer<'a> {
    snapshot: &'a ShadowSnapshot,
    scene: &'a CadScene,
    options: &'a DrawingHealthOptions,
    resources: &'a DrawingHealthResources,
    issues: Vec<DrawingHealthIssue>,
    work: i64,
    complete: bool,
    limitations: Vec<String>,
    owners: HashMap<ObjectId, ObjectId>,
    users: HashMap<ObjectId, HashSet<ObjectId>>,
    duplicates: HashMap<String, ObjectId>,
    endpoints: Vec<EndpointEntry>,
}

impl<'a> Analyzer<'a> {
    fn new(
        snapshot: &'a ShadowSnapshot,
        scene: &'a CadScene,
        options: &'a DrawingHealthOptions,
        resources: &'a DrawingHealthResources,
    ) -> Self {
        Self {
            snapshot,
            scene,
            options,
            resources,
            issues: Vec::new(),
            work: 0,
            complete: true,
            limitations: Vec::new(),
            owners: HashMap::new(),
            users: HashMap::new(),
            duplicates: HashMap::new(),
            endpoints: Vec::new(),
        }
    }

    fn tick(&mut self) -> bool {
        self.work += 1;
        if self.work > self.options.maximum_work
            || self.issues.len() >= self.options.maximum_issues
        {
            self.complete = false;
            return false;
        }
        true
    }

    #[allow(clippy::too_many_arguments)]
    fn add_issue(
        &mut self,
        code: &str,
        severity: DiagnosticSeverity,
        object: Option<ObjectId>,
        path: Option<&str>,
        message: String,
        related_objects: Vec<ObjectId>,
        position: Option<Vec3>,
    ) {
        if self.issues.len() >= self.options.maximum_issues {
            self.complete = false;
            return;
        }
        let idx = self.issues.len();
        self.issues.push(DrawingHealthIssue {
            id: format!("{code}:{idx}"),
            code: code.to_string(),
            severity,
            object,
            path: path.filter(|s| !s.is_empty()).map(str::to_string),
            position,
            related_objects,
            message,
        });
    }

    fn run(&mut self) -> DrawingHealthReport {
        if self.snapshot.objects.len() > self.options.maximum_objects {
            self.complete = false;
            self.limitations
                .push("Snapshot object count exceeded maximum_objects budget.".into());
            return self.finish();
        }

        self.build_ownership_and_references();
        if self.complete {
            self.check_geometry();
        }
        if self.complete && self.options.find_tiny_gaps && self.options.gap_tolerance > 0.0 {
            self.check_gaps();
        }
        if self.complete {
            self.check_resources();
        }
        if self.complete {
            self.check_block_cycles();
        }
        if self.complete {
            self.check_scene_occurrences();
        }

        self.finish()
    }

    fn finish(&mut self) -> DrawingHealthReport {
        DrawingHealthReport {
            document_id: self.snapshot.document_id.clone(),
            revision: self.snapshot.revision,
            complete: self.complete,
            work: self.work,
            coverage: Some(if self.complete {
                "complete".into()
            } else {
                "partial".into()
            }),
            limitations: std::mem::take(&mut self.limitations),
            issues: std::mem::take(&mut self.issues),
        }
    }

    fn build_ownership_and_references(&mut self) {
        // Track owners from Block.Entities and Document.Entities, and reference users.
        for node in self.snapshot.objects.values() {
            if !self.tick() {
                return;
            }
            if node.kind.contains(ShadowObjectKind::BLOCK) {
                let reader = self.snapshot.read(node.id.clone());
                for ent in reader.field("Entities").items() {
                    if let Some(eid) = ent.id() {
                        self.owners.insert(eid.clone(), node.id.clone());
                    }
                }
            }
            for val in node.properties.values() {
                self.collect_refs(val, &node.id, 0);
            }
        }
    }

    fn collect_refs(&mut self, value: &ShadowValue, referrer: &ObjectId, depth: usize) {
        if depth > self.options.maximum_value_depth {
            return;
        }
        match value {
            ShadowValue::Reference(target) => {
                self.users
                    .entry(target.id.clone())
                    .or_default()
                    .insert(referrer.clone());
            }
            ShadowValue::Sequence(seq) => {
                for item in &seq.items {
                    self.collect_refs(item, referrer, depth + 1);
                }
            }
            ShadowValue::Structure(st) => {
                for v in st.fields.values() {
                    self.collect_refs(v, referrer, depth + 1);
                }
            }
            _ => {}
        }
    }

    fn check_geometry(&mut self) {
        let mut entity_ids: Vec<ObjectId> = self.snapshot.entity_ids.iter().cloned().collect();
        entity_ids.sort_by(|a, b| a.0.cmp(&b.0));

        for id in entity_ids {
            if !self.tick() {
                return;
            }
            let e = self.snapshot.read(id.clone());
            if !e.exists() {
                continue;
            }
            let ty = e.type_name().to_string();

            if e.field("Normal").exists() && e.field("Normal").vector(Vec3::ZERO).length() == 0.0 {
                self.add_issue(
                    "geometry.invalid",
                    DiagnosticSeverity::Error,
                    Some(id.clone()),
                    Some("Normal"),
                    "The extrusion normal has zero length.".into(),
                    Vec::new(),
                    None,
                );
            }
            if matches!(ty.as_str(), "Circle" | "Arc") && e.field("Radius").number(0.0) <= 0.0 {
                self.add_issue(
                    "geometry.invalid",
                    DiagnosticSeverity::Error,
                    Some(id.clone()),
                    Some("Radius"),
                    "Radius must be positive.".into(),
                    Vec::new(),
                    None,
                );
            }
            if ty == "Ellipse" {
                let maj = e.field("MajorAxis").number(0.0);
                let min = e.field("MinorAxis").number(0.0);
                if maj <= 0.0 || min <= 0.0 || min > maj {
                    self.add_issue(
                        "geometry.invalid",
                        DiagnosticSeverity::Error,
                        Some(id.clone()),
                        Some("MajorAxis"),
                        "Ellipse axes must be positive and the major axis at least as long as the minor axis.".into(),
                        Vec::new(),
                        None,
                    );
                }
            }
            if ty == "Insert" {
                let s = e.field("Scale").vector(Vec3::new(1.0, 1.0, 1.0));
                if s.x == 0.0 || s.y == 0.0 || s.z == 0.0 {
                    self.add_issue(
                        "geometry.invalid",
                        DiagnosticSeverity::Error,
                        Some(id.clone()),
                        Some("Scale"),
                        "INSERT scale components must be nonzero.".into(),
                        Vec::new(),
                        None,
                    );
                }
                if !e.field("Block").exists() {
                    self.add_issue(
                        "geometry.invalid",
                        DiagnosticSeverity::Error,
                        Some(id.clone()),
                        Some("Block"),
                        "INSERT has no block definition.".into(),
                        Vec::new(),
                        None,
                    );
                }
            }
            if matches!(
                ty.as_str(),
                "Text" | "MText" | "Attribute" | "AttributeDefinition"
            ) && e.field("Height").number(0.0) <= 0.0
            {
                self.add_issue(
                    "geometry.invalid",
                    DiagnosticSeverity::Error,
                    Some(id.clone()),
                    Some("Height"),
                    "Text height must be positive.".into(),
                    Vec::new(),
                    None,
                );
            }
            if matches!(ty.as_str(), "Polyline2D" | "Polyline3D")
                && e.field("Vertexes").items().len() < 2
            {
                self.add_issue(
                    "geometry.invalid",
                    DiagnosticSeverity::Error,
                    Some(id.clone()),
                    Some("Vertexes"),
                    "Polyline has fewer than two vertices.".into(),
                    Vec::new(),
                    None,
                );
            }

            let owner = self
                .owners
                .get(&id)
                .cloned()
                .unwrap_or_else(|| self.snapshot.root.clone());
            let normal = e.field("Normal").vector(Vec3::UNIT_Z);
            let mut dup_key: Option<String> = None;

            match ty.as_str() {
                "Line" => {
                    let a = e.field("StartPoint").vector(Vec3::ZERO);
                    let b = e.field("EndPoint").vector(Vec3::ZERO);
                    if a.is_finite() && b.is_finite() {
                        if Vec3::distance(a, b) <= self.options.tiny_length {
                            self.add_issue(
                                "geometry.tiny",
                                DiagnosticSeverity::Warning,
                                Some(id.clone()),
                                None,
                                "LINE length is at or below the tiny-length threshold.".into(),
                                Vec::new(),
                                Some(a),
                            );
                        }
                        self.push_endpoint(&id, &owner, a);
                        self.push_endpoint(&id, &owner, b);
                        let av = fmt_vec(a);
                        let bv = fmt_vec(b);
                        dup_key = Some(if av <= bv {
                            format!("{av};{bv}")
                        } else {
                            format!("{bv};{av}")
                        });
                    }
                }
                "Point" => {
                    dup_key = Some(fmt_vec(e.field("Position").vector(Vec3::ZERO)));
                }
                "Circle" => {
                    dup_key = Some(format!(
                        "{};{};{}",
                        fmt_vec(e.field("Center").vector(Vec3::ZERO)),
                        fmt_num(e.field("Radius").number(0.0)),
                        fmt_vec(normal)
                    ));
                }
                "Arc" => {
                    let center = e.field("Center").vector(Vec3::ZERO);
                    let radius = e.field("Radius").number(0.0);
                    let start_ang = e.field("StartAngle").number(0.0);
                    let end_ang = e.field("EndAngle").number(0.0);
                    dup_key = Some(format!(
                        "{};{};{};{};{}",
                        fmt_vec(center),
                        fmt_num(radius),
                        fmt_vec(normal),
                        fmt_num(start_ang),
                        fmt_num(end_ang)
                    ));
                    if normal.is_finite() && normal.length() > 0.0 {
                        let basis = Affine3::object_coordinates(normal);
                        for angle in [start_ang, end_ang] {
                            let r = angle.to_radians();
                            let pt = center
                                + basis.x * (radius * r.cos())
                                + basis.y * (radius * r.sin());
                            self.push_endpoint(&id, &owner, pt);
                        }
                    }
                }
                "Ellipse" => {
                    let parts: Vec<String> = [
                        "Center",
                        "Normal",
                        "MajorAxis",
                        "MinorAxis",
                        "Rotation",
                        "StartAngle",
                        "EndAngle",
                    ]
                    .iter()
                    .map(|p| {
                        let f = e.field(p);
                        if matches!(f.value, ShadowValue::Structure(_)) {
                            fmt_vec(f.vector(Vec3::ZERO))
                        } else {
                            fmt_num(f.number(0.0))
                        }
                    })
                    .collect();
                    dup_key = Some(parts.join(";"));
                }
                "Polyline2D" | "Polyline3D" => {
                    if !e.field("IsClosed").boolean(false) {
                        let verts = e.field("Vertexes").items();
                        if let (Some(first), Some(last)) = (verts.first(), verts.last()) {
                            let is_2d = ty == "Polyline2D";
                            if !is_2d || (normal.is_finite() && normal.length() > 0.0) {
                                let frame = if is_2d {
                                    Affine3::object_coordinates(normal)
                                } else {
                                    Affine3::IDENTITY
                                };
                                let elev = e.field("Elevation").number(0.0);
                                for v in [first, last] {
                                    let p = if v.field("Position").exists() {
                                        v.field("Position").vector(Vec3::ZERO)
                                    } else {
                                        v.vector(Vec3::ZERO)
                                    };
                                    let pt = if is_2d {
                                        frame.point(Vec3::new(p.x, p.y, elev))
                                    } else {
                                        frame.point(p)
                                    };
                                    self.push_endpoint(&id, &owner, pt);
                                }
                            }
                        }
                    }
                }
                _ => {}
            }

            if self.options.find_duplicates {
                if let Some(k) = dup_key {
                    let full_key = format!("{}:{}:{}", owner.0, ty, k);
                    if let Some(previous) = self.duplicates.get(&full_key).cloned() {
                        self.add_issue(
                            "geometry.duplicate",
                            DiagnosticSeverity::Warning,
                            Some(id.clone()),
                            None,
                            "Exact geometry duplicates another entity in the same owner. Appearance may differ.".into(),
                            vec![previous],
                            None,
                        );
                    } else {
                        self.duplicates.insert(full_key, id.clone());
                    }
                }
            }
        }
    }

    fn push_endpoint(&mut self, entity: &ObjectId, owner: &ObjectId, point: Vec3) {
        if point.is_finite() {
            self.endpoints.push(EndpointEntry {
                entity: entity.clone(),
                owner: owner.clone(),
                point,
            });
        }
    }

    fn check_gaps(&mut self) {
        let mut cells: HashMap<(ObjectId, i64, i64, i64), Vec<usize>> = HashMap::new();
        let mut emitted: HashSet<(ObjectId, ObjectId)> = HashSet::new();
        let tol = self.options.gap_tolerance;

        for i in 0..self.endpoints.len() {
            if !self.tick() {
                return;
            }
            let p = self.endpoints[i].point;
            let owner = self.endpoints[i].owner.clone();
            let entity_i = self.endpoints[i].entity.clone();
            let xx = (p.x / tol).floor();
            let yy = (p.y / tol).floor();
            let zz = (p.z / tol).floor();
            if xx.abs() >= 9e18 || yy.abs() >= 9e18 || zz.abs() >= 9e18 {
                self.complete = false;
                continue;
            }
            let (x, y, z) = (xx as i64, yy as i64, zz as i64);

            for dx in -1..=1 {
                for dy in -1..=1 {
                    for dz in -1..=1 {
                        if !self.tick() {
                            return;
                        }
                        let Some(candidates) = cells.get(&(owner.clone(), x + dx, y + dy, z + dz))
                        else {
                            continue;
                        };
                        let candidates_copy: Vec<usize> = candidates.clone();
                        for j in candidates_copy {
                            if !self.tick() {
                                return;
                            }
                            let other_ent = self.endpoints[j].entity.clone();
                            let other_pt = self.endpoints[j].point;
                            if entity_i == other_ent {
                                continue;
                            }
                            let dist = Vec3::distance(p, other_pt);
                            if dist <= self.options.tiny_length || dist > tol {
                                continue;
                            }
                            let pair = if entity_i.0 < other_ent.0 {
                                (entity_i.clone(), other_ent)
                            } else {
                                (other_ent, entity_i.clone())
                            };
                            if emitted.insert(pair.clone()) {
                                let mid = (p + other_pt) * 0.5;
                                self.add_issue(
                                    "geometry.tiny-gap",
                                    DiagnosticSeverity::Warning,
                                    Some(pair.0),
                                    None,
                                    format!(
                                        "Endpoints are separated by {} drawing units.",
                                        fmt_num(dist)
                                    ),
                                    vec![pair.1],
                                    Some(mid),
                                );
                            }
                        }
                    }
                }
            }

            cells.entry((owner, x, y, z)).or_default().push(i);
        }
    }

    fn check_resources(&mut self) {
        if self.resources.available_shx_fonts.len() > self.options.maximum_objects {
            self.complete = false;
            return;
        }
        let available_fonts: HashSet<String> = self
            .resources
            .available_shx_fonts
            .iter()
            .map(|s| s.to_ascii_lowercase())
            .collect();

        let table_names = ["Blocks", "Layers", "Linetypes", "TextStyles", "ShapeStyles"];
        let doc = self.snapshot.document();
        let table_ids: HashSet<ObjectId> = table_names
            .iter()
            .filter_map(|t| doc.field(t).id().cloned())
            .collect();

        let mut block_users: HashSet<ObjectId> = HashSet::new();
        let mut font_references: HashSet<String> = HashSet::new();

        for node in self.snapshot.objects.values() {
            if !self.tick() {
                return;
            }
            let reader = self.snapshot.read(node.id.clone());
            if node.type_name == "Insert" {
                if let Some(bid) = reader.field("Block").id() {
                    block_users.insert(bid.clone());
                }
            }
            if node.kind.contains(ShadowObjectKind::LAYOUT) {
                if let Some(lbid) = reader.field("AssociatedBlock").id() {
                    block_users.insert(lbid.clone());
                }
            }
        }

        let vars = doc.field("DrawingVariables");
        for (table, header) in [
            ("Layers", "CLayer"),
            ("Linetypes", "CeLtype"),
            ("TextStyles", "TextStyle"),
        ] {
            if !self.tick() {
                return;
            }
            let current = vars.field(header).text().to_string();
            if !current.is_empty() {
                let exists = doc
                    .field(table)
                    .items()
                    .iter()
                    .any(|r| r.field("Name").text().eq_ignore_ascii_case(&current));
                if !exists {
                    self.add_issue(
                        "resource.current",
                        DiagnosticSeverity::Error,
                        Some(self.snapshot.root.clone()),
                        Some(&format!("DrawingVariables.{header}")),
                        format!("Current resource '{current}' is absent from {table}."),
                        Vec::new(),
                        None,
                    );
                }
            }
        }

        for table in table_names {
            let mut items: Vec<ShadowReader<'_>> = doc.field(table).items();
            items.sort_by(|a, b| {
                a.id()
                    .map(|i| i.0.as_str())
                    .unwrap_or("")
                    .cmp(b.id().map(|i| i.0.as_str()).unwrap_or(""))
            });
            for resource in items {
                if !self.tick() {
                    return;
                }
                let Some(id) = resource.id().cloned() else {
                    continue;
                };
                let name = resource.field("Name").text().to_string();
                let current = match table {
                    "Linetypes" => vars.field("CeLtype").text().to_string(),
                    "TextStyles" => vars.field("TextStyle").text().to_string(),
                    _ => String::new(),
                };
                let reserved = resource.field("IsReserved").boolean(false)
                    || name.starts_with('*')
                    || (table == "TextStyles" && name.eq_ignore_ascii_case("Standard"))
                    || (table == "Linetypes"
                        && ["ByLayer", "ByBlock", "Continuous"]
                            .iter()
                            .any(|n| n.eq_ignore_ascii_case(&name)));
                let used = if table == "Blocks" {
                    block_users.contains(&id)
                } else {
                    self.users.get(&id).is_some_and(|refs| {
                        refs.iter()
                            .any(|r| *r != id && *r != self.snapshot.root && !table_ids.contains(r))
                    })
                };

                if self.options.find_unused_resources
                    && table != "Layers"
                    && !reserved
                    && !used
                    && !name.eq_ignore_ascii_case(&current)
                {
                    let suffix = match table {
                        "Blocks" => "block",
                        "Linetypes" => "linetype",
                        _ => "font-style",
                    };
                    self.add_issue(
                        &format!("resource.unused-{suffix}"),
                        DiagnosticSeverity::Information,
                        Some(id.clone()),
                        Some("Name"),
                        format!(
                            "Unused {} definition '{name}'. No deletion was performed.",
                            resource.type_name()
                        ),
                        Vec::new(),
                        None,
                    );
                }

                if matches!(table, "TextStyles" | "ShapeStyles") {
                    let props: &[&str] = if table == "TextStyles" {
                        &["FontFile", "BigFont"]
                    } else {
                        &["File"]
                    };
                    for prop in props {
                        if !self.tick() {
                            return;
                        }
                        let file = resource.field(prop).text().to_string();
                        if file.is_empty() {
                            continue;
                        }
                        let basename = file
                            .rsplit(['/', '\\'])
                            .next()
                            .unwrap_or(&file)
                            .to_string();
                        font_references.insert(file.to_ascii_lowercase());
                        font_references.insert(basename.to_ascii_lowercase());
                        if !file.to_ascii_lowercase().ends_with(".shx")
                            || self.resources.available_shx_fonts.is_empty()
                        {
                            continue;
                        }
                        if !available_fonts.contains(&file.to_ascii_lowercase())
                            && !available_fonts.contains(&basename.to_ascii_lowercase())
                        {
                            self.add_issue(
                                "resource.missing-shx",
                                DiagnosticSeverity::Warning,
                                Some(id.clone()),
                                Some(prop),
                                format!(
                                    "SHX reference '{file}' is absent from the supplied host resource inventory."
                                ),
                                Vec::new(),
                                None,
                            );
                        }
                    }
                }
            }
        }

        if self.options.find_unused_resources && !self.resources.available_shx_fonts.is_empty() {
            for font in &self.resources.available_shx_fonts {
                if !self.tick() {
                    return;
                }
                if !font_references.contains(&font.to_ascii_lowercase()) {
                    self.add_issue(
                        "resource.unused-font",
                        DiagnosticSeverity::Information,
                        None,
                        Some(font),
                        format!(
                            "Host SHX alias '{font}' has no matching document font reference. It may still be used as a fallback; no resource was removed."
                        ),
                        Vec::new(),
                        None,
                    );
                }
            }
        }
    }

    fn check_block_cycles(&mut self) {
        let mut graph: BTreeMap<ObjectId, Vec<ObjectId>> = BTreeMap::new();
        for node in self.snapshot.objects.values() {
            if !node.kind.contains(ShadowObjectKind::BLOCK) {
                continue;
            }
            if !self.tick() {
                return;
            }
            let mut edges = Vec::new();
            let reader = self.snapshot.read(node.id.clone());
            for ent in reader.field("Entities").items() {
                if !self.tick() {
                    return;
                }
                if ent.type_name() == "Insert" {
                    if let Some(next) = ent.field("Block").id() {
                        edges.push(next.clone());
                    }
                }
            }
            edges.sort_by(|a, b| a.0.cmp(&b.0));
            graph.insert(node.id.clone(), edges);
        }

        let mut colors: HashMap<ObjectId, u8> = HashMap::new();
        let keys: Vec<ObjectId> = graph.keys().cloned().collect();
        for start in keys {
            if colors.contains_key(&start) {
                continue;
            }
            let mut stack: Vec<(ObjectId, usize)> = vec![(start.clone(), 0)];
            colors.insert(start, 1);
            while let Some((id, index)) = stack.pop() {
                if !self.tick() {
                    return;
                }
                let edges = graph.get(&id);
                if edges.map_or(true, |e| index >= e.len()) {
                    colors.insert(id, 2);
                    continue;
                }
                stack.push((id.clone(), index + 1));
                let next = edges.unwrap()[index].clone();
                let color = colors.get(&next).copied().unwrap_or(0);
                if color == 1 {
                    self.add_issue(
                        "block.recursion",
                        DiagnosticSeverity::Error,
                        Some(id),
                        Some("Entities"),
                        "Recursive INSERT definitions cannot be expanded finitely.".into(),
                        vec![next],
                        None,
                    );
                } else if color == 0 {
                    colors.insert(next.clone(), 1);
                    stack.push((next, 0));
                }
            }
        }
    }

    fn check_scene_occurrences(&mut self) {
        for (i, occ) in self.scene.occurrences.iter().enumerate() {
            if !self.tick() {
                return;
            }
            if !occ.bounds.is_empty()
                && occ.bounds.width() < self.options.tiny_length
                && occ.bounds.height() < self.options.tiny_length
                && occ.geometry.points.is_empty()
            {
                self.add_issue(
                    "TINY_GEOMETRY",
                    DiagnosticSeverity::Warning,
                    Some(occ.entity.clone()),
                    None,
                    format!("Entity occurrence {i} has degenerate near-zero bounds."),
                    Vec::new(),
                    None,
                );
            }
            for img in &occ.geometry.images {
                if !self.resources.available_images.is_empty()
                    && !self.resources.available_images.contains(&img.file_name)
                {
                    self.add_issue(
                        "MISSING_IMAGE",
                        DiagnosticSeverity::Warning,
                        Some(occ.entity.clone()),
                        None,
                        format!("Referenced image '{}' not found.", img.file_name),
                        Vec::new(),
                        None,
                    );
                }
            }
        }
    }
}

fn fmt_num(v: f64) -> String {
    if v == 0.0 {
        "0".to_string()
    } else {
        format!("{v:.12}")
            .trim_end_matches('0')
            .trim_end_matches('.')
            .to_string()
    }
}

fn fmt_vec(v: Vec3) -> String {
    format!("{},{},{}", fmt_num(v.x), fmt_num(v.y), fmt_num(v.z))
}
