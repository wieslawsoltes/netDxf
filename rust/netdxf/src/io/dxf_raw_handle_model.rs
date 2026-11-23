use bitflags::bitflags;

/// Role of a handle occurrence inside a raw DXF record.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum DxfRawHandleRole {
    Identity,
    Owner,
    SoftPointer,
    HardPointer,
    SoftOwner,
    HardOwner,
    Reactor,
    ExtensionDictionary,
    XData,
    Arbitrary,
    HeaderSeed,
    HeaderReference,
    Opaque,
}

/// Classification of handle graph diagnostics produced while indexing a raw DXF document.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum DxfRawHandleDiagnosticKind {
    DuplicateIdentity,
    MultipleIdentities,
    NullIdentity,
    UnresolvedReference,
    AmbiguousReference,
    MultipleOwners,
    InvalidControlGroup,
    OwnerCycle,
}

/// A single handle occurrence in a raw DXF document.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfRawHandleOccurrence {
    pub section_name: String,
    pub record_index: usize,
    pub tag_index: usize,
    pub group_code: i32,
    pub handle: u64,
    pub role: DxfRawHandleRole,
    pub subclass: Option<String>,
}

impl DxfRawHandleOccurrence {
    pub fn canonical_handle(&self) -> String {
        format!("{:X}", self.handle)
    }

    pub fn numeric_handle(&self) -> u64 {
        self.handle
    }

    pub fn code(&self) -> i32 {
        self.group_code
    }

    pub fn context(&self) -> &str {
        &self.section_name
    }

    pub fn record(&self) -> usize {
        self.record_index
    }

    pub fn is_reference(&self) -> bool {
        !matches!(self.role, DxfRawHandleRole::Identity)
    }
}

/// A single diagnostic emitted while indexing or validating raw DXF handle graphs.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DxfRawHandleDiagnostic {
    pub kind: DxfRawHandleDiagnosticKind,
    pub handle: u64,
    pub message: String,
    pub record_index: usize,
    pub tag_index_value: usize,
}

impl DxfRawHandleDiagnostic {
    pub fn record(&self) -> usize {
        self.record_index
    }

    pub fn tag_index(&self) -> usize {
        self.tag_index_value
    }
}

/// Resource limits and traversal options for raw DXF handle indexing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DxfRawHandleIndexOptions {
    pub maximum_records: usize,
    pub maximum_diagnostics: usize,
    pub maximum_occurrences: usize,
}

impl Default for DxfRawHandleIndexOptions {
    fn default() -> Self {
        Self {
            maximum_records: 1_000_000,
            maximum_diagnostics: 100_000,
            maximum_occurrences: 5_000_000,
        }
    }
}

bitflags! {
    /// Selects which handle reference categories are followed when computing a dependency closure.
    #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
    pub struct DxfRawReferenceTraversal: u32 {
        const NONE = 0;
        const HARD_POINTERS = 1;
        const SOFT_POINTERS = 2;
        const HARD_OWNERSHIP = 4;
        const SOFT_OWNERSHIP = 8;
        const PARENT_OWNERS = 16;
        const REACTORS = 32;
        const XDATA = 64;
        const HEADER_REFERENCES = 128;
        const ALL = 255;
    }
}

/// Result of computing a raw DXF handle dependency closure.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DxfRawDependencyClosure {
    pub visited_handles: Vec<u64>,
    pub unresolved_handles: Vec<u64>,
    pub ambiguous_references: Vec<u64>,
    pub uninterpreted_handles: Vec<u64>,
}

impl DxfRawDependencyClosure {
    pub fn records(&self) -> &[u64] {
        &self.visited_handles
    }

    pub fn unresolved_references(&self) -> &[u64] {
        &self.unresolved_handles
    }

    pub fn are_selected_references_resolved(&self) -> bool {
        self.unresolved_handles.is_empty() && self.ambiguous_references.is_empty()
    }
}
