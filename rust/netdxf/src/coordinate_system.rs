/// Defines the coordinate system for point transformations.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum CoordinateSystem {
    /// World Coordinate System (WCS).
    World,
    /// Object Coordinate System (OCS / ECS).
    Object,
}
