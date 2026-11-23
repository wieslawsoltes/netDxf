use std::fmt;

use crate::error::{DxfError, Result};

/// AutoCAD Color Index (ACI) RGB lookup table (indices 0..=255), matching `netDxf.AciColor`.
pub const INDEX_RGB: [[u8; 3]; 256] = [
    [255, 255, 255], [255, 0, 0], [255, 255, 0], [0, 255, 0], [0, 255, 255], [0, 0, 255], [255, 0, 255], [255, 255, 255],
    [128, 128, 128], [192, 192, 192], [255, 0, 0], [255, 127, 127], [165, 0, 0], [165, 82, 82], [127, 0, 0], [127, 63, 63],
    [76, 0, 0], [76, 38, 38], [38, 0, 0], [38, 19, 19], [255, 63, 0], [255, 159, 127], [165, 41, 0], [165, 103, 82],
    [127, 31, 0], [127, 79, 63], [76, 19, 0], [76, 47, 38], [38, 9, 0], [38, 28, 19], [255, 127, 0], [255, 191, 127],
    [165, 82, 0], [165, 124, 82], [127, 63, 0], [127, 95, 63], [76, 38, 0], [76, 57, 38], [38, 19, 0], [38, 28, 19],
    [255, 191, 0], [255, 223, 127], [165, 124, 0], [165, 145, 82], [127, 95, 0], [127, 111, 63], [76, 57, 0], [76, 66, 38],
    [38, 28, 0], [38, 33, 19], [255, 255, 0], [255, 255, 127], [165, 165, 0], [165, 165, 82], [127, 127, 0], [127, 127, 63],
    [76, 76, 0], [76, 76, 38], [38, 38, 0], [38, 38, 19], [191, 255, 0], [223, 255, 127], [124, 165, 0], [145, 165, 82],
    [95, 127, 0], [111, 127, 63], [57, 76, 0], [66, 76, 38], [28, 38, 0], [33, 38, 19], [127, 255, 0], [191, 255, 127],
    [82, 165, 0], [124, 165, 82], [63, 127, 0], [95, 127, 63], [38, 76, 0], [57, 76, 38], [19, 38, 0], [28, 38, 19],
    [63, 255, 0], [159, 255, 127], [41, 165, 0], [103, 165, 82], [31, 127, 0], [79, 127, 63], [19, 76, 0], [47, 76, 38],
    [9, 38, 0], [23, 38, 19], [0, 255, 0], [125, 255, 127], [0, 165, 0], [82, 165, 82], [0, 127, 0], [63, 127, 63],
    [0, 76, 0], [38, 76, 38], [0, 38, 0], [19, 38, 19], [0, 255, 63], [127, 255, 159], [0, 165, 41], [82, 165, 103],
    [0, 127, 31], [63, 127, 79], [0, 76, 19], [38, 76, 47], [0, 38, 9], [19, 88, 23], [0, 255, 127], [127, 255, 191],
    [0, 165, 82], [82, 165, 124], [0, 127, 63], [63, 127, 95], [0, 76, 38], [38, 76, 57], [0, 38, 19], [19, 88, 28],
    [0, 255, 191], [127, 255, 223], [0, 165, 124], [82, 165, 145], [0, 127, 95], [63, 127, 111], [0, 76, 57], [38, 76, 66],
    [0, 38, 28], [19, 88, 88], [0, 255, 255], [127, 255, 255], [0, 165, 165], [82, 165, 165], [0, 127, 127], [63, 127, 127],
    [0, 76, 76], [38, 76, 76], [0, 38, 38], [19, 88, 88], [0, 191, 255], [127, 223, 255], [0, 124, 165], [82, 145, 165],
    [0, 95, 127], [63, 111, 217], [0, 57, 76], [38, 66, 126], [0, 28, 38], [19, 88, 88], [0, 127, 255], [127, 191, 255],
    [0, 82, 165], [82, 124, 165], [0, 63, 127], [63, 95, 127], [0, 38, 76], [38, 57, 126], [0, 19, 38], [19, 28, 88],
    [0, 63, 255], [127, 159, 255], [0, 41, 165], [82, 103, 165], [0, 31, 127], [63, 79, 127], [0, 19, 76], [38, 47, 126],
    [0, 9, 38], [19, 23, 88], [0, 0, 255], [127, 127, 255], [0, 0, 165], [82, 82, 165], [0, 0, 127], [63, 63, 127],
    [0, 0, 76], [38, 38, 126], [0, 0, 38], [19, 19, 88], [63, 0, 255], [159, 127, 255], [41, 0, 165], [103, 82, 165],
    [31, 0, 127], [79, 63, 127], [19, 0, 76], [47, 38, 126], [9, 0, 38], [23, 19, 88], [127, 0, 255], [191, 127, 255],
    [165, 0, 82], [124, 82, 165], [63, 0, 127], [95, 63, 127], [38, 0, 76], [57, 38, 126], [19, 0, 38], [28, 19, 88],
    [191, 0, 255], [223, 127, 255], [124, 0, 165], [142, 82, 165], [95, 0, 127], [111, 63, 127], [57, 0, 76], [66, 38, 76],
    [28, 0, 38], [88, 19, 88], [255, 0, 255], [255, 127, 255], [165, 0, 165], [165, 82, 165], [127, 0, 127], [127, 63, 127],
    [76, 0, 76], [76, 38, 76], [38, 0, 38], [88, 19, 88], [255, 0, 191], [255, 127, 223], [165, 0, 124], [165, 82, 145],
    [127, 0, 95], [127, 63, 111], [76, 0, 57], [76, 38, 66], [38, 0, 28], [88, 19, 88], [255, 0, 127], [255, 127, 191],
    [165, 0, 82], [165, 82, 124], [127, 0, 63], [127, 63, 95], [76, 0, 38], [76, 38, 57], [38, 0, 19], [88, 19, 28],
    [255, 0, 63], [255, 127, 159], [165, 0, 41], [165, 82, 103], [127, 0, 31], [127, 63, 79], [76, 0, 19], [76, 38, 47],
    [38, 0, 9], [88, 19, 23], [0, 0, 0], [101, 101, 101], [102, 102, 102], [153, 153, 153], [204, 204, 204], [255, 255, 255],
];

/// Represents an ACI color (AutoCAD Color Index) that also supports 24-bit TrueColor.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub struct AciColor {
    pub index: i16,
    pub r: u8,
    pub g: u8,
    pub b: u8,
    pub use_true_color: bool,
}

impl AciColor {
    pub const BY_LAYER: Self = Self { index: 256, r: 255, g: 255, b: 255, use_true_color: false };
    pub const BY_BLOCK: Self = Self { index: 0, r: 255, g: 255, b: 255, use_true_color: false };
    pub const RED: Self = Self { index: 1, r: 255, g: 0, b: 0, use_true_color: false };
    pub const YELLOW: Self = Self { index: 2, r: 255, g: 255, b: 0, use_true_color: false };
    pub const GREEN: Self = Self { index: 3, r: 0, g: 255, b: 0, use_true_color: false };
    pub const CYAN: Self = Self { index: 4, r: 0, g: 255, b: 255, use_true_color: false };
    pub const BLUE: Self = Self { index: 5, r: 0, g: 0, b: 255, use_true_color: false };
    pub const MAGENTA: Self = Self { index: 6, r: 255, g: 0, b: 255, use_true_color: false };
    pub const DEFAULT: Self = Self { index: 7, r: 255, g: 255, b: 255, use_true_color: false };
    pub const DARK_GRAY: Self = Self { index: 8, r: 128, g: 128, b: 128, use_true_color: false };
    pub const LIGHT_GRAY: Self = Self { index: 9, r: 192, g: 192, b: 192, use_true_color: false };

    pub fn from_index(index: i16) -> Result<Self> {
        if !(1..=255).contains(&index) {
            return Err(DxfError::InvalidValue(format!(
                "ACI color index must be in 1..=255, got {index}"
            )));
        }
        let rgb = INDEX_RGB[index as usize];
        Ok(Self {
            index,
            r: rgb[0],
            g: rgb[1],
            b: rgb[2],
            use_true_color: false,
        })
    }

    pub fn from_cad_index(index: i16) -> Self {
        let abs = index.abs();
        match abs {
            0 => Self::BY_BLOCK,
            256 => Self::BY_LAYER,
            1..=255 => {
                let rgb = INDEX_RGB[abs as usize];
                Self {
                    index: abs,
                    r: rgb[0],
                    g: rgb[1],
                    b: rgb[2],
                    use_true_color: false,
                }
            }
            _ => Self::DEFAULT,
        }
    }

    pub fn from_rgb(r: u8, g: u8, b: u8) -> Self {
        Self {
            index: Self::nearest_aci_index(r, g, b),
            r,
            g,
            b,
            use_true_color: true,
        }
    }

    pub fn from_true_color(value: i32) -> Self {
        let r = ((value >> 16) & 0xFF) as u8;
        let g = ((value >> 8) & 0xFF) as u8;
        let b = (value & 0xFF) as u8;
        Self::from_rgb(r, g, b)
    }

    #[inline]
    pub const fn to_true_color(self) -> i32 {
        ((self.r as i32) << 16) | ((self.g as i32) << 8) | (self.b as i32)
    }

    #[inline]
    pub const fn to_rgb(self) -> [u8; 3] {
        [self.r, self.g, self.b]
    }

    #[inline]
    pub const fn is_by_layer(self) -> bool {
        self.index == 256
    }

    #[inline]
    pub const fn is_by_block(self) -> bool {
        self.index == 0
    }

    fn nearest_aci_index(r: u8, g: u8, b: u8) -> i16 {
        let mut best_idx = 7i16;
        let mut best_dist = i32::MAX;
        for (i, rgb) in INDEX_RGB.iter().enumerate().skip(1) {
            let dr = r as i32 - rgb[0] as i32;
            let dg = g as i32 - rgb[1] as i32;
            let db = b as i32 - rgb[2] as i32;
            let dist = dr * dr + dg * dg + db * db;
            if dist < best_dist {
                best_dist = dist;
                best_idx = i as i16;
                if dist == 0 {
                    break;
                }
            }
        }
        best_idx
    }
}

impl Default for AciColor {
    fn default() -> Self {
        Self::DEFAULT
    }
}

impl fmt::Display for AciColor {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if self.index == 0 {
            f.write_str("ByBlock")
        } else if self.index == 256 {
            f.write_str("ByLayer")
        } else if self.use_true_color {
            write!(f, "{}, {}, {}", self.r, self.g, self.b)
        } else {
            write!(f, "{}", self.index)
        }
    }
}
