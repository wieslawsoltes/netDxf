use std::fs::{self, File};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

use crate::error::Result;

static ATOMIC_SEQ: AtomicU64 = AtomicU64::new(0);

/// Crash-safe atomic file writer (`netDxf.IO.DxfAtomicFile`).
///
/// Writes into a sibling temporary file in the target directory, flushes it, and atomically
/// renames it into place so interrupted saves never leave a truncated or half-written DXF file.
pub struct DxfAtomicFile;

impl DxfAtomicFile {
    pub fn write_all(path: impl AsRef<Path>, bytes: &[u8]) -> Result<()> {
        let target = path.as_ref();
        let parent = target.parent().unwrap_or_else(|| Path::new("."));
        if !parent.as_os_str().is_empty() && !parent.exists() {
            fs::create_dir_all(parent)?;
        }
        let seq = ATOMIC_SEQ.fetch_add(1, Ordering::Relaxed);
        let pid = std::process::id();
        let file_name = target
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or("output.dxf");
        let tmp_path: PathBuf = parent.join(format!(".{file_name}.tmp.{pid}.{seq}"));

        let write_res = (|| -> Result<()> {
            let mut f = File::create(&tmp_path)?;
            f.write_all(bytes)?;
            f.sync_all()?;
            Ok(())
        })();

        if let Err(err) = write_res {
            let _ = fs::remove_file(&tmp_path);
            return Err(err);
        }

        if let Err(err) = fs::rename(&tmp_path, target) {
            let _ = fs::remove_file(&tmp_path);
            return Err(err.into());
        }
        Ok(())
    }
}
