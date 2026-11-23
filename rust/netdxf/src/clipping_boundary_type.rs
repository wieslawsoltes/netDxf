/// Defines the clipping boundary type for images, underlays, and wipeouts.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum ClippingBoundaryType {
    Rectangular = 1,
    Polygonal = 2,
}
