use std::fmt;
use std::str::FromStr;

use crate::error::{DxfError, Result};

/// The AutoCAD drawing database version number (`$ACADVER`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum DxfVersion {
    Unknown,
    AutoCad1_1,
    AutoCad1_2,
    AutoCad1_4,
    AutoCad2_0,
    AutoCad2_10,
    AutoCad2_5,
    AutoCad2_6,
    AutoCad9,
    AutoCad10,
    AutoCad12,
    AutoCad13,
    AutoCad14,
    #[default]
    AutoCad2000,
    AutoCad2004,
    AutoCad2007,
    AutoCad2010,
    AutoCad2013,
    AutoCad2018,
}

impl DxfVersion {
    pub const fn as_code(self) -> &'static str {
        match self {
            Self::Unknown => "Unknown",
            Self::AutoCad1_1 => "MC0.0",
            Self::AutoCad1_2 => "AC1.2",
            Self::AutoCad1_4 => "AC1.4",
            Self::AutoCad2_0 => "AC1.50",
            Self::AutoCad2_10 => "AC2.10",
            Self::AutoCad2_5 => "AC1002",
            Self::AutoCad2_6 => "AC1003",
            Self::AutoCad9 => "AC1004",
            Self::AutoCad10 => "AC1006",
            Self::AutoCad12 => "AC1009",
            Self::AutoCad13 => "AC1012",
            Self::AutoCad14 => "AC1014",
            Self::AutoCad2000 => "AC1015",
            Self::AutoCad2004 => "AC1018",
            Self::AutoCad2007 => "AC1021",
            Self::AutoCad2010 => "AC1024",
            Self::AutoCad2013 => "AC1027",
            Self::AutoCad2018 => "AC1032",
        }
    }

    pub fn from_code(code: &str) -> Self {
        match code.trim().to_ascii_uppercase().as_str() {
            "MC0.0" => Self::AutoCad1_1,
            "AC1.2" => Self::AutoCad1_2,
            "AC1.4" => Self::AutoCad1_4,
            "AC1.50" => Self::AutoCad2_0,
            "AC2.10" => Self::AutoCad2_10,
            "AC1002" => Self::AutoCad2_5,
            "AC1003" => Self::AutoCad2_6,
            "AC1004" => Self::AutoCad9,
            "AC1006" => Self::AutoCad10,
            "AC1009" => Self::AutoCad12,
            "AC1012" => Self::AutoCad13,
            "AC1014" => Self::AutoCad14,
            "AC1015" => Self::AutoCad2000,
            "AC1018" => Self::AutoCad2004,
            "AC1021" => Self::AutoCad2007,
            "AC1024" => Self::AutoCad2010,
            "AC1027" => Self::AutoCad2013,
            "AC1032" => Self::AutoCad2018,
            _ => Self::Unknown,
        }
    }

    #[inline]
    pub const fn uses_utf8(self) -> bool {
        matches!(
            self,
            Self::AutoCad2007 | Self::AutoCad2010 | Self::AutoCad2013 | Self::AutoCad2018
        )
    }

    #[inline]
    pub const fn is_supported(self) -> bool {
        matches!(
            self,
            Self::AutoCad12
                | Self::AutoCad2000
                | Self::AutoCad2004
                | Self::AutoCad2007
                | Self::AutoCad2010
                | Self::AutoCad2013
                | Self::AutoCad2018
        )
    }
}

impl fmt::Display for DxfVersion {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_code())
    }
}

impl FromStr for DxfVersion {
    type Err = DxfError;
    fn from_str(s: &str) -> Result<Self> {
        let v = Self::from_code(s);
        if v == Self::Unknown {
            Err(DxfError::VersionNotSupported {
                version: s.to_string(),
            })
        } else {
            Ok(v)
        }
    }
}
