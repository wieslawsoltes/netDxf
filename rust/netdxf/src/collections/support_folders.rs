use std::path::{Path, PathBuf};

/// Manages search paths for DXF external support files (fonts, linetypes, patterns, images).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct SupportFolders {
    pub folders: Vec<PathBuf>,
}

impl SupportFolders {
    pub fn new() -> Self {
        Self { folders: Vec::new() }
    }

    pub fn add(&mut self, folder: impl Into<PathBuf>) {
        self.folders.push(folder.into());
    }

    pub fn find_file(&self, file_name: &str) -> Option<PathBuf> {
        let direct = Path::new(file_name);
        if direct.exists() {
            return Some(direct.to_path_buf());
        }
        let base = direct.file_name()?;
        for folder in &self.folders {
            let candidate = folder.join(base);
            if candidate.exists() {
                return Some(candidate);
            }
        }
        None
    }
}
