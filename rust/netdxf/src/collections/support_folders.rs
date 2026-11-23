use std::path::{Path, PathBuf};

/// Manages search paths for DXF external support files (fonts, linetypes, patterns, images).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct SupportFolders {
    pub folders: Vec<PathBuf>,
}

impl SupportFolders {
    pub fn new() -> Self {
        Self {
            folders: Vec::new(),
        }
    }

    #[inline]
    pub fn count(&self) -> usize {
        self.folders.len()
    }

    #[inline]
    pub fn is_read_only(&self) -> bool {
        false
    }

    pub fn add(&mut self, folder: impl Into<PathBuf>) {
        self.folders.push(folder.into());
    }

    pub fn add_range(&mut self, folders: impl IntoIterator<Item = PathBuf>) {
        self.folders.extend(folders);
    }

    pub fn clear(&mut self) {
        self.folders.clear();
    }

    pub fn contains(&self, folder: impl AsRef<Path>) -> bool {
        let target = folder.as_ref();
        self.folders.iter().any(|f| f == target)
    }

    pub fn index_of(&self, folder: impl AsRef<Path>) -> Option<usize> {
        let target = folder.as_ref();
        self.folders.iter().position(|f| f == target)
    }

    pub fn insert(&mut self, index: usize, folder: impl Into<PathBuf>) {
        let idx = index.min(self.folders.len());
        self.folders.insert(idx, folder.into());
    }

    pub fn remove(&mut self, folder: impl AsRef<Path>) -> bool {
        if let Some(idx) = self.index_of(folder) {
            self.folders.remove(idx);
            true
        } else {
            false
        }
    }

    pub fn remove_at(&mut self, index: usize) -> Option<PathBuf> {
        if index < self.folders.len() {
            Some(self.folders.remove(index))
        } else {
            None
        }
    }

    pub fn copy_to(&self, dest: &mut Vec<PathBuf>) {
        dest.extend(self.folders.iter().cloned());
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
