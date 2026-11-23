use std::fmt;

use crate::error::{DxfError, Result};

/// Represents the transparency of a layer or an entity (`0..=90`, `-1` = ByLayer, `100` = ByBlock).
#[derive(Clone, Copy, Debug)]
pub struct Transparency {
    value: i16,
    stored_alpha_value: Option<i32>,
}

impl Transparency {
    pub const BY_LAYER: Self = Self { value: -1, stored_alpha_value: None };
    pub const BY_BLOCK: Self = Self { value: 100, stored_alpha_value: None };

    pub fn new(value: i16) -> Result<Self> {
        if !(0..=90).contains(&value) {
            return Err(DxfError::InvalidValue(format!(
                "Accepted transparency values range from 0 to 90, got {value}"
            )));
        }
        Ok(Self { value, stored_alpha_value: None })
    }

    #[inline]
    pub const fn value(self) -> i16 {
        self.value
    }

    #[inline]
    pub const fn is_by_layer(self) -> bool {
        self.value == -1
    }

    #[inline]
    pub const fn is_by_block(self) -> bool {
        self.value == 100
    }

    #[inline]
    pub const fn stored_alpha_value(self) -> Option<i32> {
        self.stored_alpha_value
    }

    pub fn set_value(&mut self, value: i16) -> Result<()> {
        if !(0..=90).contains(&value) {
            return Err(DxfError::InvalidValue(format!(
                "Accepted transparency values range from 0 to 90, got {value}"
            )));
        }
        self.value = value;
        self.stored_alpha_value = None;
        Ok(())
    }

    pub fn to_alpha_value(self) -> i32 {
        if let Some(stored) = self.stored_alpha_value {
            return stored;
        }
        let alpha = (255.0 * (100 - self.value) as f64 / 100.0) as u8;
        let bytes = if self.is_by_block() {
            [0u8, 0, 0, 1]
        } else {
            [alpha, 0, 0, 2]
        };
        i32::from_le_bytes(bytes)
    }

    pub fn from_alpha_value(value: i32) -> Self {
        let bytes = value.to_le_bytes();
        let alpha = (100.0 - (bytes[0] as f64 / 255.0) * 100.0) as i16;
        let mut result = Self::from_cad_index(alpha);
        result.stored_alpha_value = Some(value);
        result
    }

    pub fn from_layer_state_alpha_value(value: i32) -> Self {
        if value == 0 {
            Self { value: 0, stored_alpha_value: Some(0) }
        } else {
            Self::from_alpha_value(value)
        }
    }

    pub fn from_cad_index(alpha: i16) -> Self {
        match alpha {
            -1 => Self::BY_LAYER,
            100 => Self::BY_BLOCK,
            v if v < 0 => Self { value: 0, stored_alpha_value: None },
            v if v > 90 => Self { value: 90, stored_alpha_value: None },
            v => Self { value: v, stored_alpha_value: None },
        }
    }
}

impl Default for Transparency {
    fn default() -> Self {
        Self::BY_LAYER
    }
}

impl PartialEq for Transparency {
    fn eq(&self, other: &Self) -> bool {
        self.value == other.value
    }
}

impl Eq for Transparency {}

impl fmt::Display for Transparency {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if self.is_by_layer() {
            f.write_str("ByLayer")
        } else if self.is_by_block() {
            f.write_str("ByBlock")
        } else {
            write!(f, "{}", self.value)
        }
    }
}
