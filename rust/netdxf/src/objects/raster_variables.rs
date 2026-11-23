use crate::dxf_object::DxfObject;
use crate::dxf_object_code::DxfObjectCode;
use crate::units::ImageUnits;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum ImageDisplayQuality {
    Draft = 0,
    #[default]
    High = 1,
}

/// Represents DXF `RASTERVARIABLES` object.
#[derive(Clone, Debug, PartialEq)]
pub struct RasterVariables {
    pub object: DxfObject,
    pub display_frame: bool,
    pub display_quality: ImageDisplayQuality,
    pub units: ImageUnits,
}

impl Default for RasterVariables {
    fn default() -> Self {
        Self {
            object: DxfObject::new(DxfObjectCode::RASTER_VARIABLES),
            display_frame: true,
            display_quality: ImageDisplayQuality::High,
            units: ImageUnits::Unitless,
        }
    }
}
