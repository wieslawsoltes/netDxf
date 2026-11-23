use rustc_hash::{FxHashMap, FxHashSet};

use crate::error::{DxfError, Result};
use crate::tables::dxf_table_cell_address::DxfTableCellAddress;

/// Scalar value returned by a cell resolver during formula evaluation.
#[derive(Clone, Debug, PartialEq)]
pub enum TableCellValue {
    Empty,
    Text(String),
    Int(i32),
    Float(f64),
    Formula(String),
}

#[derive(Clone, Debug)]
enum Expr {
    Number(f64),
    Cell(DxfTableCellAddress),
    UnaryNeg(Box<Expr>),
    Binary {
        op: BinOp,
        left: Box<Expr>,
        right: Box<Expr>,
    },
    Aggregate {
        func: AggFunc,
        args: Vec<AggArg>,
    },
}

#[derive(Clone, Copy, Debug)]
enum BinOp {
    Add,
    Sub,
    Mul,
    Div,
    Pow,
}

#[derive(Clone, Copy, Debug)]
enum AggFunc {
    Sum,
    Average,
    Count,
    Min,
    Max,
}

#[derive(Clone, Debug)]
enum AggArg {
    Range(DxfTableCellAddress, DxfTableCellAddress),
    Expr(Expr),
}

/// Bounded, safe arithmetic table formula (`netDxf.Tables.DxfTableFormula`).
///
/// Supports invariant numbers, A1 cell references, rectangular ranges (`A1:C5`) in
/// `SUM`/`AVERAGE`/`COUNT`/`MIN`/`MAX`, unary signs, parentheses, `+`, `-`, `*`, `/`,
/// and right-associative `^`.
#[derive(Clone, Debug)]
pub struct DxfTableFormula {
    pub expression: String,
    root: Expr,
    scalar_only: bool,
}

impl DxfTableFormula {
    pub const MAXIMUM_LENGTH: usize = 4096;
    pub const MAXIMUM_DEPTH: usize = 128;
    pub const MAXIMUM_OPS: usize = 1_000_000;

    pub fn parse(expression: &str) -> Result<Self> {
        Self::parse_internal(expression, true)
    }

    pub fn parse_scalar(expression: &str) -> Result<Self> {
        Self::parse_internal(expression, false)
    }

    fn parse_internal(expression: &str, allow_cells: bool) -> Result<Self> {
        if expression.len() < 2
            || expression.len() > Self::MAXIMUM_LENGTH
            || !expression.starts_with('=')
        {
            return Err(DxfError::InvalidValue(
                "A bounded table formula must start with '='".to_string(),
            ));
        }
        let mut parser = FormulaParser {
            input: expression.as_bytes(),
            pos: 1,
            allow_cells,
            depth: 0,
        };
        let root = parser.parse_add_sub()?;
        parser.skip_ws();
        if parser.pos != parser.input.len() {
            return Err(DxfError::InvalidValue(format!(
                "Unexpected trailing characters in formula '{expression}'"
            )));
        }
        Ok(Self {
            expression: expression.to_string(),
            root,
            scalar_only: !allow_cells,
        })
    }

    pub fn evaluate_scalar(&self) -> Result<f64> {
        if !self.scalar_only {
            return Err(DxfError::InvalidValue(
                "Compile with parse_scalar before scalar evaluation".to_string(),
            ));
        }
        self.evaluate(1, 1, |_| TableCellValue::Empty)
    }

    pub fn evaluate<F>(&self, row_count: usize, col_count: usize, mut resolver: F) -> Result<f64>
    where
        F: FnMut(DxfTableCellAddress) -> TableCellValue,
    {
        if row_count == 0 || col_count == 0 || row_count.saturating_mul(col_count) > 1_000_000 {
            return Err(DxfError::InvalidValue(
                "Table grid dimensions must be in 1..=1_000_000 cells".to_string(),
            ));
        }
        let mut ctx = EvalCtx {
            rows: row_count,
            cols: col_count,
            remaining_ops: Self::MAXIMUM_OPS,
            cache: FxHashMap::default(),
            visiting: FxHashSet::default(),
        };
        Self::eval_expr(&self.root, &mut ctx, &mut resolver)
    }

    fn eval_expr<F>(expr: &Expr, ctx: &mut EvalCtx, resolver: &mut F) -> Result<f64>
    where
        F: FnMut(DxfTableCellAddress) -> TableCellValue,
    {
        ctx.step()?;
        let val = match expr {
            Expr::Number(n) => *n,
            Expr::Cell(addr) => match Self::resolve_cell(*addr, ctx, resolver)? {
                Some(v) => v,
                None => {
                    return Err(DxfError::InvalidValue(format!(
                        "Cell {addr} is empty or non-numeric in arithmetic expression"
                    )));
                }
            },
            Expr::UnaryNeg(inner) => -Self::eval_expr(inner, ctx, resolver)?,
            Expr::Binary { op, left, right } => {
                let l = Self::eval_expr(left, ctx, resolver)?;
                let r = Self::eval_expr(right, ctx, resolver)?;
                match op {
                    BinOp::Add => l + r,
                    BinOp::Sub => l - r,
                    BinOp::Mul => l * r,
                    BinOp::Div => l / r,
                    BinOp::Pow => l.powf(r),
                }
            }
            Expr::Aggregate { func, args } => {
                let mut values = Vec::new();
                for arg in args {
                    match arg {
                        AggArg::Range(a, b) => {
                            let r0 = a.row.min(b.row);
                            let r1 = a.row.max(b.row);
                            let c0 = a.column.min(b.column);
                            let c1 = a.column.max(b.column);
                            for r in r0..=r1 {
                                for c in c0..=c1 {
                                    if let Some(v) = Self::resolve_cell(
                                        DxfTableCellAddress::new(r, c),
                                        ctx,
                                        resolver,
                                    )? {
                                        values.push(v);
                                    }
                                }
                            }
                        }
                        AggArg::Expr(Expr::Cell(addr)) => {
                            if let Some(v) = Self::resolve_cell(*addr, ctx, resolver)? {
                                values.push(v);
                            }
                        }
                        AggArg::Expr(e) => {
                            values.push(Self::eval_expr(e, ctx, resolver)?);
                        }
                    }
                }
                match func {
                    AggFunc::Sum => {
                        // Neumaier compensated summation for exact cancellation (e.g. SUM(1e16, 1, -1e16) == 1).
                        let mut sum = 0.0_f64;
                        let mut comp = 0.0_f64;
                        for &v in &values {
                            let t = sum + v;
                            if sum.abs() >= v.abs() {
                                comp += (sum - t) + v;
                            } else {
                                comp += (v - t) + sum;
                            }
                            sum = t;
                        }
                        sum + comp
                    }
                    AggFunc::Count => values.len() as f64,
                    AggFunc::Average => {
                        if values.is_empty() {
                            return Err(DxfError::InvalidValue(
                                "AVERAGE over empty range".to_string(),
                            ));
                        }
                        // Overflow-safe compensated mean using scaling by max magnitude.
                        let max_abs = values.iter().map(|v| v.abs()).fold(0.0_f64, f64::max);
                        if max_abs == 0.0 {
                            0.0
                        } else {
                            let mut sum = 0.0_f64;
                            let mut comp = 0.0_f64;
                            for &v in &values {
                                let s = v / max_abs;
                                let t = sum + s;
                                if sum.abs() >= s.abs() {
                                    comp += (sum - t) + s;
                                } else {
                                    comp += (s - t) + sum;
                                }
                                sum = t;
                            }
                            ((sum + comp) / values.len() as f64) * max_abs
                        }
                    }
                    AggFunc::Min => values
                        .iter()
                        .copied()
                        .reduce(f64::min)
                        .ok_or_else(|| DxfError::InvalidValue("MIN over empty range".to_string()))?,
                    AggFunc::Max => values
                        .iter()
                        .copied()
                        .reduce(f64::max)
                        .ok_or_else(|| DxfError::InvalidValue("MAX over empty range".to_string()))?,
                }
            }
        };
        if !val.is_finite() {
            return Err(DxfError::InvalidValue(
                "Table formula result is not finite".to_string(),
            ));
        }
        Ok(if val == 0.0 { 0.0 } else { val })
    }

    fn resolve_cell<F>(
        addr: DxfTableCellAddress,
        ctx: &mut EvalCtx,
        resolver: &mut F,
    ) -> Result<Option<f64>>
    where
        F: FnMut(DxfTableCellAddress) -> TableCellValue,
    {
        ctx.step()?;
        if addr.row >= ctx.rows || addr.column >= ctx.cols {
            return Err(DxfError::InvalidValue(format!(
                "Table formula reference {addr} is outside the grid"
            )));
        }
        if let Some(&cached) = ctx.cache.get(&addr) {
            return Ok(cached);
        }
        if !ctx.visiting.insert(addr) {
            return Err(DxfError::InvalidValue(format!(
                "Cyclic cell reference detected at {addr}"
            )));
        }
        let result = match resolver(addr) {
            TableCellValue::Empty | TableCellValue::Text(_) => Ok(None),
            TableCellValue::Int(i) => Ok(Some(i as f64)),
            TableCellValue::Float(f) => {
                if !f.is_finite() {
                    Err(DxfError::InvalidValue(format!(
                        "Non-finite cell value at {addr}"
                    )))
                } else {
                    Ok(Some(f))
                }
            }
            TableCellValue::Formula(expr_str) => {
                let sub = DxfTableFormula::parse(&expr_str)?;
                let v = Self::eval_expr(&sub.root, ctx, resolver)?;
                Ok(Some(v))
            }
        };
        ctx.visiting.remove(&addr);
        if let Ok(val) = result {
            ctx.cache.insert(addr, val);
        }
        result
    }
}

struct EvalCtx {
    rows: usize,
    cols: usize,
    remaining_ops: usize,
    cache: FxHashMap<DxfTableCellAddress, Option<f64>>,
    visiting: FxHashSet<DxfTableCellAddress>,
}

impl EvalCtx {
    #[inline]
    fn step(&mut self) -> Result<()> {
        if self.remaining_ops == 0 {
            return Err(DxfError::InvalidValue(
                "Table calculation exceeded operation budget".to_string(),
            ));
        }
        self.remaining_ops -= 1;
        Ok(())
    }
}

struct FormulaParser<'a> {
    input: &'a [u8],
    pos: usize,
    allow_cells: bool,
    depth: usize,
}

impl<'a> FormulaParser<'a> {
    fn skip_ws(&mut self) {
        while self.pos < self.input.len() && self.input[self.pos].is_ascii_whitespace() {
            self.pos += 1;
        }
    }

    fn peek(&mut self) -> Option<u8> {
        self.skip_ws();
        self.input.get(self.pos).copied()
    }

    fn enter(&mut self) -> Result<()> {
        self.depth += 1;
        if self.depth > DxfTableFormula::MAXIMUM_DEPTH {
            return Err(DxfError::InvalidValue(
                "Formula nesting depth exceeds maximum".to_string(),
            ));
        }
        Ok(())
    }

    fn leave(&mut self) {
        self.depth = self.depth.saturating_sub(1);
    }

    fn parse_add_sub(&mut self) -> Result<Expr> {
        self.enter()?;
        let mut left = self.parse_mul_div()?;
        while let Some(b) = self.peek() {
            let op = match b {
                b'+' => BinOp::Add,
                b'-' => BinOp::Sub,
                _ => break,
            };
            self.pos += 1;
            let right = self.parse_mul_div()?;
            left = Expr::Binary {
                op,
                left: Box::new(left),
                right: Box::new(right),
            };
        }
        self.leave();
        Ok(left)
    }

    fn parse_mul_div(&mut self) -> Result<Expr> {
        self.enter()?;
        let mut left = self.parse_unary()?;
        while let Some(b) = self.peek() {
            let op = match b {
                b'*' => BinOp::Mul,
                b'/' => BinOp::Div,
                _ => break,
            };
            self.pos += 1;
            let right = self.parse_unary()?;
            left = Expr::Binary {
                op,
                left: Box::new(left),
                right: Box::new(right),
            };
        }
        self.leave();
        Ok(left)
    }

    fn parse_unary(&mut self) -> Result<Expr> {
        self.enter()?;
        let res = match self.peek() {
            Some(b'+') => {
                self.pos += 1;
                self.parse_unary()
            }
            Some(b'-') => {
                self.pos += 1;
                Ok(Expr::UnaryNeg(Box::new(self.parse_unary()?)))
            }
            _ => self.parse_power(),
        };
        self.leave();
        res
    }

    fn parse_power(&mut self) -> Result<Expr> {
        let base = self.parse_primary()?;
        if self.peek() == Some(b'^') {
            self.pos += 1;
            let exp = self.parse_unary()?;
            return Ok(Expr::Binary {
                op: BinOp::Pow,
                left: Box::new(base),
                right: Box::new(exp),
            });
        }
        Ok(base)
    }

    fn parse_primary(&mut self) -> Result<Expr> {
        match self.peek() {
            Some(b'(') => {
                self.pos += 1;
                let expr = self.parse_add_sub()?;
                if self.peek() != Some(b')') {
                    return Err(DxfError::InvalidValue("Unclosed '(' in formula".to_string()));
                }
                self.pos += 1;
                Ok(expr)
            }
            Some(b) if b.is_ascii_digit() || b == b'.' => self.parse_number(),
            Some(b) if b.is_ascii_alphabetic() || b == b'$' => self.parse_ident_or_cell(),
            _ => Err(DxfError::InvalidValue(
                "Unexpected token in table formula".to_string(),
            )),
        }
    }

    fn parse_number(&mut self) -> Result<Expr> {
        let start = self.pos;
        while self.pos < self.input.len() && self.input[self.pos].is_ascii_digit() {
            self.pos += 1;
        }
        if self.pos < self.input.len() && self.input[self.pos] == b'.' {
            self.pos += 1;
            while self.pos < self.input.len() && self.input[self.pos].is_ascii_digit() {
                self.pos += 1;
            }
        }
        if self.pos < self.input.len()
            && matches!(self.input[self.pos], b'e' | b'E')
        {
            self.pos += 1;
            if self.pos < self.input.len() && matches!(self.input[self.pos], b'+' | b'-') {
                self.pos += 1;
            }
            while self.pos < self.input.len() && self.input[self.pos].is_ascii_digit() {
                self.pos += 1;
            }
        }
        let s = std::str::from_utf8(&self.input[start..self.pos]).unwrap_or("");
        let n: f64 = s
            .parse()
            .map_err(|_| DxfError::InvalidValue(format!("Invalid number '{s}' in formula")))?;
        Ok(Expr::Number(n))
    }

    fn parse_ident_or_cell(&mut self) -> Result<Expr> {
        let start = self.pos;
        while self.pos < self.input.len()
            && (self.input[self.pos].is_ascii_alphanumeric() || self.input[self.pos] == b'$')
        {
            self.pos += 1;
        }
        let ident = std::str::from_utf8(&self.input[start..self.pos]).unwrap_or("");
        if self.peek() == Some(b'(') {
            let func = match ident.to_ascii_uppercase().as_str() {
                "SUM" => AggFunc::Sum,
                "AVERAGE" | "AVG" => AggFunc::Average,
                "COUNT" => AggFunc::Count,
                "MIN" => AggFunc::Min,
                "MAX" => AggFunc::Max,
                _ => {
                    return Err(DxfError::InvalidValue(format!(
                        "Unknown formula function '{ident}'"
                    )));
                }
            };
            self.pos += 1; // consume '('
            let mut args = Vec::new();
            if self.peek() == Some(b')') {
                return Err(DxfError::InvalidValue(format!(
                    "Function '{ident}' requires at least one argument"
                )));
            }
            loop {
                let saved = self.pos;
                if let Some(range) = self.try_parse_range()? {
                    args.push(range);
                } else {
                    self.pos = saved;
                    args.push(AggArg::Expr(self.parse_add_sub()?));
                }
                if self.peek() == Some(b',') {
                    self.pos += 1;
                } else {
                    break;
                }
            }
            if self.peek() != Some(b')') {
                return Err(DxfError::InvalidValue(
                    "Missing ')' in aggregate call".to_string(),
                ));
            }
            self.pos += 1;
            Ok(Expr::Aggregate { func, args })
        } else {
            if !self.allow_cells {
                return Err(DxfError::InvalidValue(format!(
                    "Cell reference '{ident}' not permitted in scalar formula"
                )));
            }
            let addr = DxfTableCellAddress::parse_a1(ident)?;
            Ok(Expr::Cell(addr))
        }
    }

    fn try_parse_range(&mut self) -> Result<Option<AggArg>> {
        self.skip_ws();
        let start = self.pos;
        while self.pos < self.input.len()
            && (self.input[self.pos].is_ascii_alphanumeric() || self.input[self.pos] == b'$')
        {
            self.pos += 1;
        }
        let first = std::str::from_utf8(&self.input[start..self.pos]).unwrap_or("");
        if self.peek() != Some(b':') {
            return Ok(None);
        }
        if !self.allow_cells {
            return Err(DxfError::InvalidValue(
                "Range references not permitted in scalar formula".to_string(),
            ));
        }
        self.pos += 1; // consume ':'
        self.skip_ws();
        let start2 = self.pos;
        while self.pos < self.input.len()
            && (self.input[self.pos].is_ascii_alphanumeric() || self.input[self.pos] == b'$')
        {
            self.pos += 1;
        }
        let second = std::str::from_utf8(&self.input[start2..self.pos]).unwrap_or("");
        let a = DxfTableCellAddress::parse_a1(first)?;
        let b = DxfTableCellAddress::parse_a1(second)?;
        Ok(Some(AggArg::Range(a, b)))
    }
}
