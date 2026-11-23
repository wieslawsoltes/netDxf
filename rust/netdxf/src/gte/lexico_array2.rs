use crate::gte::GTE;

/// Provides 2D array access conforming to row-major or column-major order.
#[derive(Debug, Clone, PartialEq)]
pub struct LexicoArray2 {
    num_rows: usize,
    num_cols: usize,
    matrix: Vec<f64>,
    use_row_major: bool,
}

impl LexicoArray2 {
    pub fn new(num_rows: usize, num_cols: usize, matrix: Vec<f64>) -> Self {
        Self {
            num_rows,
            num_cols,
            matrix,
            use_row_major: GTE::USE_ROW_MAJOR,
        }
    }

    pub fn with_layout(
        num_rows: usize,
        num_cols: usize,
        matrix: Vec<f64>,
        use_row_major: bool,
    ) -> Self {
        Self {
            num_rows,
            num_cols,
            matrix,
            use_row_major,
        }
    }

    pub fn num_rows(&self) -> usize {
        self.num_rows
    }

    pub fn num_cols(&self) -> usize {
        self.num_cols
    }

    #[inline]
    fn index(&self, r: usize, c: usize) -> usize {
        if self.use_row_major {
            c + self.num_cols * r
        } else {
            r + self.num_rows * c
        }
    }

    pub fn get(&self, r: usize, c: usize) -> f64 {
        self.matrix[self.index(r, c)]
    }

    pub fn set(&mut self, r: usize, c: usize, value: f64) {
        let idx = self.index(r, c);
        self.matrix[idx] = value;
    }

    pub fn as_slice(&self) -> &[f64] {
        &self.matrix
    }

    pub fn copy_to(&self, dest: &mut [f64]) {
        let n = dest.len().min(self.matrix.len());
        dest[..n].copy_from_slice(&self.matrix[..n]);
    }
}
