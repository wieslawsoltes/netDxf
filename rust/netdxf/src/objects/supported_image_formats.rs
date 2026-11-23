/// Supported raster image file formats for `ImageDefinition`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum SupportedImageFormats {
    Bmp,
    Jpeg,
    Png,
    Tiff,
}
