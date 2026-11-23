use crate::aci_color::AciColor;
use crate::bounding_rectangle::BoundingRectangle;
use crate::dxf_object::DxfObject;
use crate::entities::arc::Arc;
use crate::entities::body::Body;
use crate::entities::circle::Circle;
use crate::entities::dimension::Dimension;
use crate::entities::dxf_opaque_entity::DxfOpaqueEntity;
use crate::entities::ellipse::Ellipse;
use crate::entities::entity_type::EntityType;
use crate::entities::face3d::Face3D;
use crate::entities::hatch::Hatch;
use crate::entities::helix::Helix;
use crate::entities::image::Image;
use crate::entities::insert::Insert;
use crate::entities::leader::Leader;
use crate::entities::light::Light;
use crate::entities::line::Line;
use crate::entities::mesh::Mesh;
use crate::entities::mline::MLine;
use crate::entities::mtext::MText;
use crate::entities::multi_leader::MultiLeader;
use crate::entities::ole2_frame::{Ole2Frame, OleFrame};
use crate::entities::point::Point;
use crate::entities::polyface_mesh::PolyfaceMesh;
use crate::entities::polygon_mesh::PolygonMesh;
use crate::entities::polyline2d::Polyline2D;
use crate::entities::polyline3d::Polyline3D;
use crate::entities::ray::Ray;
use crate::entities::region::Region;
use crate::entities::section::Section;
use crate::entities::shape::Shape;
use crate::entities::solid::Solid;
use crate::entities::solid3d::Solid3D;
use crate::entities::spline::Spline;
use crate::entities::stored_table::StoredTable;
use crate::entities::text::Text;
use crate::entities::tolerance::Tolerance;
use crate::entities::trace::Trace;
use crate::entities::underlay::Underlay;
use crate::entities::viewport::Viewport;
use crate::entities::wipeout::Wipeout;
use crate::entities::xline::XLine;
use crate::lineweight::Lineweight;
use crate::matrix3::Matrix3;
use crate::tables::{Layer, Linetype};
use crate::transparency::Transparency;
use crate::vector3::Vector3;

/// Common properties shared by every DXF entity.
#[derive(Clone, Debug, PartialEq)]
pub struct EntityCommon {
    pub object: DxfObject,
    pub entity_type: EntityType,
    pub color: AciColor,
    pub layer: String,
    pub linetype: String,
    pub lineweight: Lineweight,
    pub transparency: Transparency,
    pub linetype_scale: f64,
    pub is_visible: bool,
    pub normal: Vector3,
    pub reactors: Vec<String>,
}

impl EntityCommon {
    pub fn new(entity_type: EntityType, code_name: &'static str) -> Self {
        Self {
            object: DxfObject::new(code_name),
            entity_type,
            color: AciColor::BY_LAYER,
            layer: Layer::DEFAULT_NAME.to_string(),
            linetype: Linetype::BY_LAYER_NAME.to_string(),
            lineweight: Lineweight::ByLayer,
            transparency: Transparency::BY_LAYER,
            linetype_scale: 1.0,
            is_visible: true,
            normal: Vector3::UNIT_Z,
            reactors: Vec::new(),
        }
    }

    #[inline]
    pub fn handle(&self) -> Option<&str> {
        self.object.handle.as_deref()
    }
}

/// Polymorphic DXF entity enum (`netDxf.Entities.EntityObject`).
///
/// Large variants are boxed to keep `size_of::<EntityObject>()` small and cache-friendly.
#[derive(Clone, Debug, PartialEq)]
pub enum EntityObject {
    Arc(Box<Arc>),
    Body(Box<Body>),
    Circle(Box<Circle>),
    Dimension(Box<Dimension>),
    Ellipse(Box<Ellipse>),
    Face3D(Box<Face3D>),
    Hatch(Box<Hatch>),
    Helix(Box<Helix>),
    Image(Box<Image>),
    Insert(Box<Insert>),
    Leader(Box<Leader>),
    Light(Box<Light>),
    Line(Box<Line>),
    Mesh(Box<Mesh>),
    MLine(Box<MLine>),
    MText(Box<MText>),
    MultiLeader(Box<MultiLeader>),
    Ole2Frame(Box<Ole2Frame>),
    OleFrame(Box<OleFrame>),
    Point(Box<Point>),
    PolyfaceMesh(Box<PolyfaceMesh>),
    PolygonMesh(Box<PolygonMesh>),
    Polyline2D(Box<Polyline2D>),
    Polyline3D(Box<Polyline3D>),
    Ray(Box<Ray>),
    Region(Box<Region>),
    Section(Box<Section>),
    Shape(Box<Shape>),
    Solid(Box<Solid>),
    Solid3D(Box<Solid3D>),
    Spline(Box<Spline>),
    Table(Box<StoredTable>),
    Text(Box<Text>),
    Tolerance(Box<Tolerance>),
    Trace(Box<Trace>),
    Underlay(Box<Underlay>),
    Viewport(Box<Viewport>),
    Wipeout(Box<Wipeout>),
    XLine(Box<XLine>),
    Opaque(Box<DxfOpaqueEntity>),
}

impl EntityObject {
    pub fn common(&self) -> &EntityCommon {
        match self {
            Self::Arc(e) => &e.common,
            Self::Body(e) => &e.common,
            Self::Circle(e) => &e.common,
            Self::Dimension(e) => &e.common,
            Self::Ellipse(e) => &e.common,
            Self::Face3D(e) => &e.common,
            Self::Hatch(e) => &e.common,
            Self::Helix(e) => &e.common,
            Self::Image(e) => &e.common,
            Self::Insert(e) => &e.common,
            Self::Leader(e) => &e.common,
            Self::Light(e) => &e.common,
            Self::Line(e) => &e.common,
            Self::Mesh(e) => &e.common,
            Self::MLine(e) => &e.common,
            Self::MText(e) => &e.common,
            Self::MultiLeader(e) => &e.common,
            Self::Ole2Frame(e) => &e.common,
            Self::OleFrame(e) => &e.common,
            Self::Point(e) => &e.common,
            Self::PolyfaceMesh(e) => &e.common,
            Self::PolygonMesh(e) => &e.common,
            Self::Polyline2D(e) => &e.common,
            Self::Polyline3D(e) => &e.common,
            Self::Ray(e) => &e.common,
            Self::Region(e) => &e.common,
            Self::Section(e) => &e.common,
            Self::Shape(e) => &e.common,
            Self::Solid(e) => &e.common,
            Self::Solid3D(e) => &e.common,
            Self::Spline(e) => &e.common,
            Self::Table(e) => &e.common,
            Self::Text(e) => &e.common,
            Self::Tolerance(e) => &e.common,
            Self::Trace(e) => &e.common,
            Self::Underlay(e) => &e.common,
            Self::Viewport(e) => &e.common,
            Self::Wipeout(e) => &e.common,
            Self::XLine(e) => &e.common,
            Self::Opaque(e) => &e.common,
        }
    }

    pub fn common_mut(&mut self) -> &mut EntityCommon {
        match self {
            Self::Arc(e) => &mut e.common,
            Self::Body(e) => &mut e.common,
            Self::Circle(e) => &mut e.common,
            Self::Dimension(e) => &mut e.common,
            Self::Ellipse(e) => &mut e.common,
            Self::Face3D(e) => &mut e.common,
            Self::Hatch(e) => &mut e.common,
            Self::Helix(e) => &mut e.common,
            Self::Image(e) => &mut e.common,
            Self::Insert(e) => &mut e.common,
            Self::Leader(e) => &mut e.common,
            Self::Light(e) => &mut e.common,
            Self::Line(e) => &mut e.common,
            Self::Mesh(e) => &mut e.common,
            Self::MLine(e) => &mut e.common,
            Self::MText(e) => &mut e.common,
            Self::MultiLeader(e) => &mut e.common,
            Self::Ole2Frame(e) => &mut e.common,
            Self::OleFrame(e) => &mut e.common,
            Self::Point(e) => &mut e.common,
            Self::PolyfaceMesh(e) => &mut e.common,
            Self::PolygonMesh(e) => &mut e.common,
            Self::Polyline2D(e) => &mut e.common,
            Self::Polyline3D(e) => &mut e.common,
            Self::Ray(e) => &mut e.common,
            Self::Region(e) => &mut e.common,
            Self::Section(e) => &mut e.common,
            Self::Shape(e) => &mut e.common,
            Self::Solid(e) => &mut e.common,
            Self::Solid3D(e) => &mut e.common,
            Self::Spline(e) => &mut e.common,
            Self::Table(e) => &mut e.common,
            Self::Text(e) => &mut e.common,
            Self::Tolerance(e) => &mut e.common,
            Self::Trace(e) => &mut e.common,
            Self::Underlay(e) => &mut e.common,
            Self::Viewport(e) => &mut e.common,
            Self::Wipeout(e) => &mut e.common,
            Self::XLine(e) => &mut e.common,
            Self::Opaque(e) => &mut e.common,
        }
    }

    #[inline]
    pub fn entity_type(&self) -> EntityType {
        self.common().entity_type
    }

    #[inline]
    pub fn code_name(&self) -> &str {
        &self.common().object.code_name
    }

    #[inline]
    pub fn handle(&self) -> Option<&str> {
        self.common().handle()
    }

    #[inline]
    pub fn layer(&self) -> &str {
        &self.common().layer
    }

    pub fn transform_by(&mut self, transformation: Matrix3, translation: Vector3) {
        match self {
            Self::Arc(e) => e.transform_by(transformation, translation),
            Self::Circle(e) => e.transform_by(transformation, translation),
            Self::Ellipse(e) => e.transform_by(transformation, translation),
            Self::Face3D(e) => e.transform_by(transformation, translation),
            Self::Insert(e) => e.transform_by(transformation, translation),
            Self::Line(e) => e.transform_by(transformation, translation),
            Self::Hatch(e) => e.transform_by(transformation, translation),
            Self::Helix(e) => e.transform_by(transformation, translation),
            Self::Mesh(e) => e.transform_by(transformation, translation),
            Self::MLine(e) => e.transform_by(transformation, translation),
            Self::MText(e) => e.transform_by(transformation, translation),
            Self::Ole2Frame(e) => e.transform_by(transformation, translation),
            Self::Point(e) => e.transform_by(transformation, translation),
            Self::PolyfaceMesh(e) => e.transform_by(transformation, translation),
            Self::PolygonMesh(e) => e.transform_by(transformation, translation),
            Self::Polyline2D(e) => e.transform_by(transformation, translation),
            Self::Polyline3D(e) => e.transform_by(transformation, translation),
            Self::Ray(e) => e.transform_by(transformation, translation),
            Self::Section(e) => e.transform_by(transformation, translation),
            Self::Spline(e) => e.transform_by(transformation, translation),
            Self::Text(e) => e.transform_by(transformation, translation),
            Self::Viewport(e) => e.transform_by(transformation, translation),
            Self::XLine(e) => e.transform_by(transformation, translation),
            _ => {}
        }
    }

    #[inline] pub fn color(&self) -> AciColor { self.common().color }
    #[inline] pub fn linetype(&self) -> &str { &self.common().linetype }
    #[inline] pub fn linetype_scale(&self) -> f64 { self.common().linetype_scale }
    #[inline] pub fn lineweight(&self) -> Lineweight { self.common().lineweight }
    #[inline] pub fn transparency(&self) -> Transparency { self.common().transparency }
    #[inline] pub fn is_visible(&self) -> bool { self.common().is_visible }
    #[inline] pub fn normal(&self) -> Vector3 { self.common().normal }
    pub fn clear_proxy_graphics(&mut self) {
        self.common_mut().object.x_data.shift_remove("ACAD_PROXY_ENTITY");
    }

    pub fn bounding_rectangle(&self) -> Option<BoundingRectangle> {
        match self {
            Self::Arc(e) => Some(e.bounding_rectangle()),
            Self::Circle(e) => Some(e.bounding_rectangle()),
            Self::Ellipse(e) => Some(e.bounding_rectangle()),
            Self::Face3D(e) => Some(e.bounding_rectangle()),
            Self::Insert(e) => Some(e.bounding_rectangle()),
            Self::Line(e) => Some(e.bounding_rectangle()),
            Self::Mesh(e) => e.bounding_rectangle(),
            Self::MText(e) => Some(e.bounding_rectangle()),
            Self::Point(e) => Some(e.bounding_rectangle()),
            Self::PolyfaceMesh(e) => e.bounding_rectangle(),
            Self::PolygonMesh(e) => e.bounding_rectangle(),
            Self::Polyline2D(e) => e.bounding_rectangle(),
            Self::Polyline3D(e) => e.bounding_rectangle(),
            Self::Solid(e) => Some(e.bounding_rectangle()),
            Self::Spline(e) => e.bounding_rectangle(),
            Self::Text(e) => Some(e.bounding_rectangle()),
            Self::Trace(e) => Some(e.bounding_rectangle()),
            _ => None,
        }
    }
}

macro_rules! impl_from_entity {
    ($($variant:ident => $ty:ty),* $(,)?) => {
        $(
            impl From<$ty> for EntityObject {
                #[inline]
                fn from(value: $ty) -> Self {
                    Self::$variant(Box::new(value))
                }
            }
        )*
    };
}

impl_from_entity! {
    Arc => Arc,
    Body => Body,
    Circle => Circle,
    Dimension => Dimension,
    Ellipse => Ellipse,
    Face3D => Face3D,
    Hatch => Hatch,
    Helix => Helix,
    Image => Image,
    Insert => Insert,
    Leader => Leader,
    Light => Light,
    Line => Line,
    Mesh => Mesh,
    MLine => MLine,
    MText => MText,
    MultiLeader => MultiLeader,
    Ole2Frame => Ole2Frame,
    OleFrame => OleFrame,
    Point => Point,
    PolyfaceMesh => PolyfaceMesh,
    PolygonMesh => PolygonMesh,
    Polyline2D => Polyline2D,
    Polyline3D => Polyline3D,
    Ray => Ray,
    Region => Region,
    Section => Section,
    Shape => Shape,
    Solid => Solid,
    Solid3D => Solid3D,
    Spline => Spline,
    Table => StoredTable,
    Text => Text,
    Tolerance => Tolerance,
    Trace => Trace,
    Underlay => Underlay,
    Viewport => Viewport,
    Wipeout => Wipeout,
    XLine => XLine,
    Opaque => DxfOpaqueEntity,
}

/// Entity shadow casting/receiving mode (DXF group 284).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i16)]
pub enum EntityShadowMode {
    #[default]
    CastAndReceive = 0,
    Cast = 1,
    Receive = 2,
    Ignore = 3,
}
