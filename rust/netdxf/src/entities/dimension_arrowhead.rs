use crate::blocks::Block;
use crate::entities::{Circle, EntityObject, Line, Polyline2D, Polyline2DVertex, Solid};
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Predefined AutoCAD dimension arrowhead block definitions.
pub struct DimensionArrowhead;

impl DimensionArrowhead {
    fn make_block(name: &str, entities: Vec<EntityObject>) -> Block {
        let mut block = Block::new_unchecked(name, false);
        block.entities = entities;
        block
    }

    pub fn dot() -> Block {
        let mut p = Polyline2D::new(
            vec![
                Polyline2DVertex::with_bulge(-0.25, 0.0, 1.0),
                Polyline2DVertex::with_bulge(0.25, 0.0, 1.0),
            ],
            true,
        );
        p.set_constant_width(0.5);
        Self::make_block(
            "_DOT",
            vec![
                EntityObject::Polyline2D(Box::new(p)),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-0.5, 0.0, 0.0),
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn dot_small() -> Block {
        let mut p = Polyline2D::new(
            vec![
                Polyline2DVertex::with_bulge(-0.0625, 0.0, 1.0),
                Polyline2DVertex::with_bulge(0.0625, 0.0, 1.0),
            ],
            true,
        );
        p.set_constant_width(0.125);
        Self::make_block("_DOTSMALL", vec![EntityObject::Polyline2D(Box::new(p))])
    }

    pub fn dot_blank() -> Block {
        Self::make_block(
            "_DOTBLANK",
            vec![
                EntityObject::Circle(Box::new(Circle::new(Vector3::ZERO, 0.5))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-0.5, 0.0, 0.0),
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn origin_indicator() -> Block {
        Self::make_block(
            "_ORIGIN",
            vec![
                EntityObject::Circle(Box::new(Circle::new(Vector3::ZERO, 0.5))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn origin_indicator2() -> Block {
        Self::make_block(
            "_ORIGIN2",
            vec![
                EntityObject::Circle(Box::new(Circle::new(Vector3::ZERO, 0.5))),
                EntityObject::Circle(Box::new(Circle::new(Vector3::ZERO, 0.25))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-0.5, 0.0, 0.0),
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn open() -> Block {
        Self::make_block(
            "_OPEN",
            vec![
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-1.0, 0.16666667, 0.0),
                    Vector3::ZERO,
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, -0.16666667, 0.0),
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn open90() -> Block {
        Self::make_block(
            "_OPEN90",
            vec![
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-0.5, 0.5, 0.0),
                    Vector3::ZERO,
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-0.5, -0.5, 0.0),
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn open30() -> Block {
        Self::make_block(
            "_OPEN30",
            vec![
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-1.0, 0.26794919, 0.0),
                    Vector3::ZERO,
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, -0.26794919, 0.0),
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn closed() -> Block {
        Self::make_block(
            "_CLOSED",
            vec![
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-1.0, 0.16666667, 0.0),
                    Vector3::ZERO,
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, -0.16666667, 0.0),
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-1.0, 0.16666667, 0.0),
                    Vector3::new(-1.0, -0.16666667, 0.0),
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn dot_small_blank() -> Block {
        Self::make_block(
            "_SMALL",
            vec![EntityObject::Circle(Box::new(Circle::new(
                Vector3::ZERO,
                0.25,
            )))],
        )
    }

    pub fn none() -> Block {
        Self::make_block("_NONE", Vec::new())
    }

    pub fn oblique() -> Block {
        Self::make_block(
            "_OBLIQUE",
            vec![EntityObject::Line(Box::new(Line::new(
                Vector3::new(-0.5, -0.5, 0.0),
                Vector3::new(0.5, 0.5, 0.0),
            )))],
        )
    }

    pub fn box_filled() -> Block {
        let mut s = Solid::new(
            Vector2::new(-0.5, -0.5),
            Vector2::new(0.5, -0.5),
            Vector2::new(-0.5, 0.5),
            Vector2::new(0.5, 0.5),
        );
        s.elevation = 0.0;
        Self::make_block(
            "_BOXFILLED",
            vec![
                EntityObject::Solid(Box::new(s)),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-0.5, 0.0, 0.0),
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn box_blank() -> Block {
        Self::make_block(
            "_BOXBLANK",
            vec![
                EntityObject::Polyline2D(Box::new(Polyline2D::new(
                    vec![
                        Polyline2DVertex::new(-0.5, -0.5),
                        Polyline2DVertex::new(0.5, -0.5),
                        Polyline2DVertex::new(0.5, 0.5),
                        Polyline2DVertex::new(-0.5, 0.5),
                    ],
                    true,
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-0.5, 0.0, 0.0),
                    Vector3::new(-1.0, 0.0, 0.0),
                ))),
            ],
        )
    }

    pub fn r#box() -> Block {
        Self::box_blank()
    }

    pub fn closed_blank() -> Block {
        Self::make_block(
            "_CLOSEDBLANK",
            vec![
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-1.0, 0.16666667, 0.0),
                    Vector3::ZERO,
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::ZERO,
                    Vector3::new(-1.0, -0.16666667, 0.0),
                ))),
                EntityObject::Line(Box::new(Line::new(
                    Vector3::new(-1.0, 0.16666667, 0.0),
                    Vector3::new(-1.0, -0.16666667, 0.0),
                ))),
            ],
        )
    }

    pub fn datum_triangle_filled() -> Block {
        let s = Solid::new(
            Vector2::new(0.0, 0.57735027),
            Vector2::new(0.0, -0.57735027),
            Vector2::new(-1.0, 0.0),
            Vector2::new(-1.0, 0.0),
        );
        Self::make_block("_DATUMFILLED", vec![EntityObject::Solid(Box::new(s))])
    }

    pub fn datum_triangle() -> Block {
        Self::make_block(
            "_DATUMBLANK",
            vec![EntityObject::Polyline2D(Box::new(Polyline2D::new(
                vec![
                    Polyline2DVertex::new(0.0, 0.57735027),
                    Polyline2DVertex::new(-1.0, 0.0),
                    Polyline2DVertex::new(0.0, -0.57735027),
                ],
                true,
            )))],
        )
    }

    pub fn integral() -> Block {
        Self::make_block(
            "_INTEGRAL",
            vec![EntityObject::Line(Box::new(Line::new(
                Vector3::new(-0.25, -0.5, 0.0),
                Vector3::new(0.25, 0.5, 0.0),
            )))],
        )
    }

    pub fn architectural_tick() -> Block {
        let mut p = Polyline2D::new(
            vec![
                Polyline2DVertex::new(-0.5, -0.5),
                Polyline2DVertex::new(0.5, 0.5),
            ],
            false,
        );
        p.set_constant_width(0.15);
        Self::make_block("_ARCHTICK", vec![EntityObject::Polyline2D(Box::new(p))])
    }
}
