use crate::entities::{
    Arc, Body, Circle, Dimension, Ellipse, EntityObject, Face3D, Hatch, Helix, Image, Insert,
    Leader, Light, Line, MLine, MText, Mesh, MultiLeader, Ole2Frame, OleFrame, Point, PolyfaceMesh,
    PolygonMesh, Polyline2D, Polyline3D, Ray, Region, Section, Shape, Solid, Solid3D, Spline,
    StoredTable, Text, Tolerance, Trace, Underlay, Viewport, Wipeout, XLine,
};

/// Collection of drawing entities with typed query accessors (`netDxf.Collections.DrawingEntities`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct DrawingEntities {
    all: Vec<EntityObject>,
}

impl DrawingEntities {
    pub fn new() -> Self {
        Self { all: Vec::new() }
    }

    pub fn add(&mut self, entity: impl Into<EntityObject>) {
        self.all.push(entity.into());
    }

    pub fn add_range(&mut self, entities: impl IntoIterator<Item = EntityObject>) {
        self.all.extend(entities);
    }

    pub fn clear(&mut self) {
        self.all.clear();
    }

    #[inline]
    pub fn all(&self) -> &[EntityObject] {
        &self.all
    }

    #[inline]
    pub fn all_mut(&mut self) -> &mut Vec<EntityObject> {
        &mut self.all
    }

    #[inline]
    pub fn len(&self) -> usize {
        self.all.len()
    }

    #[inline]
    pub fn is_empty(&self) -> bool {
        self.all.is_empty()
    }

    pub fn iter(&self) -> std::slice::Iter<'_, EntityObject> {
        self.all.iter()
    }

    pub fn iter_mut(&mut self) -> std::slice::IterMut<'_, EntityObject> {
        self.all.iter_mut()
    }

    pub fn arcs(&self) -> impl Iterator<Item = &Arc> {
        self.all.iter().filter_map(|e| match e { EntityObject::Arc(v) => Some(v.as_ref()), _ => None })
    }
    pub fn circles(&self) -> impl Iterator<Item = &Circle> {
        self.all.iter().filter_map(|e| match e { EntityObject::Circle(v) => Some(v.as_ref()), _ => None })
    }
    pub fn dimensions(&self) -> impl Iterator<Item = &Dimension> {
        self.all.iter().filter_map(|e| match e { EntityObject::Dimension(v) => Some(v.as_ref()), _ => None })
    }
    pub fn ellipses(&self) -> impl Iterator<Item = &Ellipse> {
        self.all.iter().filter_map(|e| match e { EntityObject::Ellipse(v) => Some(v.as_ref()), _ => None })
    }
    pub fn faces3d(&self) -> impl Iterator<Item = &Face3D> {
        self.all.iter().filter_map(|e| match e { EntityObject::Face3D(v) => Some(v.as_ref()), _ => None })
    }
    pub fn hatches(&self) -> impl Iterator<Item = &Hatch> {
        self.all.iter().filter_map(|e| match e { EntityObject::Hatch(v) => Some(v.as_ref()), _ => None })
    }
    pub fn helices(&self) -> impl Iterator<Item = &Helix> {
        self.all.iter().filter_map(|e| match e { EntityObject::Helix(v) => Some(v.as_ref()), _ => None })
    }
    pub fn images(&self) -> impl Iterator<Item = &Image> {
        self.all.iter().filter_map(|e| match e { EntityObject::Image(v) => Some(v.as_ref()), _ => None })
    }
    pub fn inserts(&self) -> impl Iterator<Item = &Insert> {
        self.all.iter().filter_map(|e| match e { EntityObject::Insert(v) => Some(v.as_ref()), _ => None })
    }
    pub fn leaders(&self) -> impl Iterator<Item = &Leader> {
        self.all.iter().filter_map(|e| match e { EntityObject::Leader(v) => Some(v.as_ref()), _ => None })
    }
    pub fn lines(&self) -> impl Iterator<Item = &Line> {
        self.all.iter().filter_map(|e| match e { EntityObject::Line(v) => Some(v.as_ref()), _ => None })
    }
    pub fn meshes(&self) -> impl Iterator<Item = &Mesh> {
        self.all.iter().filter_map(|e| match e { EntityObject::Mesh(v) => Some(v.as_ref()), _ => None })
    }
    pub fn mlines(&self) -> impl Iterator<Item = &MLine> {
        self.all.iter().filter_map(|e| match e { EntityObject::MLine(v) => Some(v.as_ref()), _ => None })
    }
    pub fn mtexts(&self) -> impl Iterator<Item = &MText> {
        self.all.iter().filter_map(|e| match e { EntityObject::MText(v) => Some(v.as_ref()), _ => None })
    }
    pub fn points(&self) -> impl Iterator<Item = &Point> {
        self.all.iter().filter_map(|e| match e { EntityObject::Point(v) => Some(v.as_ref()), _ => None })
    }
    pub fn polyface_meshes(&self) -> impl Iterator<Item = &PolyfaceMesh> {
        self.all.iter().filter_map(|e| match e { EntityObject::PolyfaceMesh(v) => Some(v.as_ref()), _ => None })
    }
    pub fn polygon_meshes(&self) -> impl Iterator<Item = &PolygonMesh> {
        self.all.iter().filter_map(|e| match e { EntityObject::PolygonMesh(v) => Some(v.as_ref()), _ => None })
    }
    pub fn polylines2d(&self) -> impl Iterator<Item = &Polyline2D> {
        self.all.iter().filter_map(|e| match e { EntityObject::Polyline2D(v) => Some(v.as_ref()), _ => None })
    }
    pub fn polylines3d(&self) -> impl Iterator<Item = &Polyline3D> {
        self.all.iter().filter_map(|e| match e { EntityObject::Polyline3D(v) => Some(v.as_ref()), _ => None })
    }
    pub fn rays(&self) -> impl Iterator<Item = &Ray> {
        self.all.iter().filter_map(|e| match e { EntityObject::Ray(v) => Some(v.as_ref()), _ => None })
    }
    pub fn shapes(&self) -> impl Iterator<Item = &Shape> {
        self.all.iter().filter_map(|e| match e { EntityObject::Shape(v) => Some(v.as_ref()), _ => None })
    }
    pub fn solids(&self) -> impl Iterator<Item = &Solid> {
        self.all.iter().filter_map(|e| match e { EntityObject::Solid(v) => Some(v.as_ref()), _ => None })
    }
    pub fn splines(&self) -> impl Iterator<Item = &Spline> {
        self.all.iter().filter_map(|e| match e { EntityObject::Spline(v) => Some(v.as_ref()), _ => None })
    }
    pub fn texts(&self) -> impl Iterator<Item = &Text> {
        self.all.iter().filter_map(|e| match e { EntityObject::Text(v) => Some(v.as_ref()), _ => None })
    }
    pub fn tolerances(&self) -> impl Iterator<Item = &Tolerance> {
        self.all.iter().filter_map(|e| match e { EntityObject::Tolerance(v) => Some(v.as_ref()), _ => None })
    }
    pub fn traces(&self) -> impl Iterator<Item = &Trace> {
        self.all.iter().filter_map(|e| match e { EntityObject::Trace(v) => Some(v.as_ref()), _ => None })
    }
    pub fn underlays(&self) -> impl Iterator<Item = &Underlay> {
        self.all.iter().filter_map(|e| match e { EntityObject::Underlay(v) => Some(v.as_ref()), _ => None })
    }
    pub fn viewports(&self) -> impl Iterator<Item = &Viewport> {
        self.all.iter().filter_map(|e| match e { EntityObject::Viewport(v) => Some(v.as_ref()), _ => None })
    }
    pub fn wipeouts(&self) -> impl Iterator<Item = &Wipeout> {
        self.all.iter().filter_map(|e| match e { EntityObject::Wipeout(v) => Some(v.as_ref()), _ => None })
    }
    pub fn xlines(&self) -> impl Iterator<Item = &XLine> {
        self.all.iter().filter_map(|e| match e { EntityObject::XLine(v) => Some(v.as_ref()), _ => None })
    }
    pub fn solids3d(&self) -> impl Iterator<Item = &Solid3D> {
        self.all.iter().filter_map(|e| match e { EntityObject::Solid3D(v) => Some(v.as_ref()), _ => None })
    }
    pub fn bodies(&self) -> impl Iterator<Item = &Body> {
        self.all.iter().filter_map(|e| match e { EntityObject::Body(v) => Some(v.as_ref()), _ => None })
    }
    pub fn regions(&self) -> impl Iterator<Item = &Region> {
        self.all.iter().filter_map(|e| match e { EntityObject::Region(v) => Some(v.as_ref()), _ => None })
    }
    pub fn tables(&self) -> impl Iterator<Item = &StoredTable> {
        self.all.iter().filter_map(|e| match e { EntityObject::Table(v) => Some(v.as_ref()), _ => None })
    }
    pub fn multi_leaders(&self) -> impl Iterator<Item = &MultiLeader> {
        self.all.iter().filter_map(|e| match e { EntityObject::MultiLeader(v) => Some(v.as_ref()), _ => None })
    }
    pub fn lights(&self) -> impl Iterator<Item = &Light> {
        self.all.iter().filter_map(|e| match e { EntityObject::Light(v) => Some(v.as_ref()), _ => None })
    }
    pub fn ole2_frames(&self) -> impl Iterator<Item = &Ole2Frame> {
        self.all.iter().filter_map(|e| match e { EntityObject::Ole2Frame(v) => Some(v.as_ref()), _ => None })
    }
    pub fn ole_frames(&self) -> impl Iterator<Item = &OleFrame> {
        self.all.iter().filter_map(|e| match e { EntityObject::OleFrame(v) => Some(v.as_ref()), _ => None })
    }
    pub fn sections(&self) -> impl Iterator<Item = &Section> {
        self.all.iter().filter_map(|e| match e { EntityObject::Section(v) => Some(v.as_ref()), _ => None })
    }

    pub fn stored_tables(&self) -> impl Iterator<Item = &StoredTable> {
        self.tables()
    }

    pub fn opaque_entities(&self) -> impl Iterator<Item = &crate::entities::DxfOpaqueEntity> {
        std::iter::empty()
    }

    #[inline]
    pub fn active_layout(&self) -> &'static str {
        "Model"
    }

    pub fn remove(&mut self, index: usize) -> Option<EntityObject> {
        if index < self.all.len() {
            Some(self.all.remove(index))
        } else {
            None
        }
    }
}
