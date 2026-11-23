use crate::ShxFont;
use netdxf_shadow::{Bounds2, Vec3};
use netdxf_shadow_geometry::{CadContour, Curve3, LineCurve};
use serde::{Deserialize, Serialize};
use std::f64::consts::PI;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShxExecutionOptions {
    pub maximum_steps: usize,
    pub maximum_recursion: usize,
    pub maximum_call_depth: usize,
    pub maximum_curves: usize,
    pub maximum_instructions: usize,
    pub maximum_stack_depth: usize,
}

impl Default for ShxExecutionOptions {
    fn default() -> Self {
        Self {
            maximum_steps: 100_000,
            maximum_recursion: 32,
            maximum_call_depth: 32,
            maximum_curves: 100_000,
            maximum_instructions: 100_000,
            maximum_stack_depth: 64,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ShxGlyph {
    pub code: u32,
    pub contours: Vec<CadContour>,
    pub advance: f64,
    pub bounds: Bounds2,
    pub executed_instructions: usize,
    pub exit_scale: f64,
}

/// Interprets AutoCAD SHX vector bytecode (`ShxGlyphCompiler.cs`), including pen up/down,
/// scaling, position stack push/pop, subshape calls (opcode 7), single & multi-XY displacements
/// (opcodes 8, 9), octant & fractional arcs (opcodes 10, 11), bulge & multi-bulge arcs
/// (opcodes 12, 13), vertical-only skip (opcode 14), and 16-direction length-angle vectors.
pub struct ShxGlyphCompiler;

impl ShxGlyphCompiler {
    pub fn compile(font: &ShxFont, code: u32, options: &ShxExecutionOptions) -> Option<ShxGlyph> {
        let _ = font.symbols.get(&code)?;
        let mut vm = ShxVm {
            font,
            options,
            x: 0.0,
            y: 0.0,
            pen_down: true,
            scale: 1.0,
            stack: Vec::new(),
            curves: Vec::new(),
            steps: 0,
            bounds: Bounds2::EMPTY,
            skip_next: false,
        };
        vm.exec_symbol(code, 0);

        let contours = if vm.curves.is_empty() {
            Vec::new()
        } else {
            vec![CadContour {
                curves: vm.curves,
                closed: false,
            }]
        };

        Some(ShxGlyph {
            code,
            contours,
            advance: vm.x,
            bounds: vm.bounds,
            executed_instructions: vm.steps,
            exit_scale: vm.scale,
        })
    }
}

struct ShxVm<'a> {
    font: &'a ShxFont,
    options: &'a ShxExecutionOptions,
    x: f64,
    y: f64,
    pen_down: bool,
    scale: f64,
    stack: Vec<(f64, f64)>,
    curves: Vec<Curve3>,
    steps: usize,
    bounds: Bounds2,
    skip_next: bool,
}

impl<'a> ShxVm<'a> {
    fn emit_line(&mut self, nx: f64, ny: f64) {
        if self.pen_down && self.curves.len() < self.options.maximum_curves {
            self.bounds = self
                .bounds
                .include(Vec3::xy(self.x, self.y))
                .include(Vec3::xy(nx, ny));
            self.curves.push(Curve3::Line(LineCurve {
                a: Vec3::xy(self.x, self.y),
                b: Vec3::xy(nx, ny),
            }));
        }
        self.x = nx;
        self.y = ny;
    }

    fn emit_bulge(&mut self, dx: f64, dy: f64, bulge_byte: i8) {
        let nx = self.x + dx;
        let ny = self.y + dy;
        if bulge_byte == 0 || !self.pen_down {
            self.emit_line(nx, ny);
            return;
        }
        let bulge = (bulge_byte as f64) / 127.0;
        let chord = dx.hypot(dy);
        if chord <= 1e-12 {
            self.x = nx;
            self.y = ny;
            return;
        }
        let theta = 4.0 * bulge.atan();
        let half = theta * 0.5;
        let sin_half = half.sin();
        if sin_half.abs() <= 1e-12 {
            self.emit_line(nx, ny);
            return;
        }
        let radius = (chord * 0.5) / sin_half.abs();
        let sagitta_offset = radius * half.cos();
        let mx = (self.x + nx) * 0.5;
        let my = (self.y + ny) * 0.5;
        let px = -dy / chord;
        let py = dx / chord;
        let sign = if bulge > 0.0 { 1.0 } else { -1.0 };
        let cx = mx + px * sagitta_offset * sign;
        let cy = my + py * sagitta_offset * sign;
        let start_ang = (self.y - cy).atan2(self.x - cx);
        let segs = ((theta.abs() / (PI / 8.0)).ceil() as usize).clamp(4, 32);
        for s in 1..=segs {
            let a = start_ang + theta * (s as f64 / segs as f64);
            let px_i = cx + radius * a.cos();
            let py_i = cy + radius * a.sin();
            self.emit_line(px_i, py_i);
        }
        self.x = nx;
        self.y = ny;
    }

    fn emit_octant_arc(&mut self, radius: f64, start_octant: i32, delta_octants: i32) {
        if radius <= 0.0 {
            return;
        }
        let start_ang = (start_octant as f64) * (PI / 4.0);
        let sweep = if delta_octants == 0 {
            2.0 * PI
        } else {
            (delta_octants as f64) * (PI / 4.0)
        };
        let cx = self.x - radius * start_ang.cos();
        let cy = self.y - radius * start_ang.sin();
        let segs = ((sweep.abs() / (PI / 8.0)).ceil() as usize).clamp(4, 32);
        for s in 1..=segs {
            let a = start_ang + sweep * (s as f64 / segs as f64);
            let nx = cx + radius * a.cos();
            let ny = cy + radius * a.sin();
            self.emit_line(nx, ny);
        }
    }

    fn exec_symbol(&mut self, code: u32, depth: usize) {
        if depth > self.options.maximum_call_depth || depth > self.options.maximum_recursion {
            return;
        }
        let Some(sym) = self.font.symbols.get(&code) else {
            return;
        };
        let data = &sym.data;
        let mut i = 0usize;
        let max_steps = self.options.maximum_steps.min(self.options.maximum_instructions);

        while i < data.len() && self.steps < max_steps {
            self.steps += 1;
            let op = data[i];
            i += 1;

            if self.skip_next {
                self.skip_next = false;
                // Skip operands of the skipped vertical-only command.
                match op {
                    3 | 4 => i = (i + 1).min(data.len()),
                    7 => i = (i + 1).min(data.len()),
                    8 => i = (i + 2).min(data.len()),
                    9 => {
                        while i + 1 < data.len() {
                            let dx = data[i];
                            let dy = data[i + 1];
                            i += 2;
                            if dx == 0 && dy == 0 {
                                break;
                            }
                        }
                    }
                    10 => i = (i + 2).min(data.len()),
                    11 => i = (i + 5).min(data.len()),
                    12 => i = (i + 3).min(data.len()),
                    13 => {
                        while i + 1 < data.len() {
                            let dx = data[i];
                            let dy = data[i + 1];
                            i += 2;
                            if dx == 0 && dy == 0 {
                                break;
                            }
                            if i < data.len() {
                                i += 1;
                            }
                        }
                    }
                    _ => {}
                }
                continue;
            }

            match op {
                0 => break, // End of shape
                1 => self.pen_down = true,
                2 => self.pen_down = false,
                3 if i < data.len() => {
                    let d = data[i] as f64;
                    if d > 0.0 {
                        self.scale /= d;
                    }
                    i += 1;
                }
                4 if i < data.len() => {
                    self.scale *= data[i] as f64;
                    i += 1;
                }
                5 => {
                    if self.stack.len() < self.options.maximum_stack_depth {
                        self.stack.push((self.x, self.y));
                    }
                }
                6 => {
                    if let Some((px, py)) = self.stack.pop() {
                        self.x = px;
                        self.y = py;
                    }
                }
                7 if i < data.len() => {
                    let sub_code = data[i] as u32;
                    i += 1;
                    self.exec_symbol(sub_code, depth + 1);
                }
                8 if i + 1 < data.len() => {
                    let dx = (data[i] as i8) as f64 * self.scale;
                    let dy = (data[i + 1] as i8) as f64 * self.scale;
                    i += 2;
                    self.emit_line(self.x + dx, self.y + dy);
                }
                9 => {
                    while i + 1 < data.len() && self.steps < max_steps {
                        let dx_b = data[i] as i8;
                        let dy_b = data[i + 1] as i8;
                        i += 2;
                        if dx_b == 0 && dy_b == 0 {
                            break;
                        }
                        self.steps += 1;
                        self.emit_line(
                            self.x + (dx_b as f64) * self.scale,
                            self.y + (dy_b as f64) * self.scale,
                        );
                    }
                }
                10 if i + 1 < data.len() => {
                    let radius = (data[i] as f64) * self.scale;
                    let spec = data[i + 1] as i8;
                    i += 2;
                    let ccw = spec >= 0;
                    let abs_spec = spec.unsigned_abs();
                    let start_oct = ((abs_spec >> 4) & 0x07) as i32;
                    let count_oct = (abs_spec & 0x07) as i32;
                    let delta = if count_oct == 0 {
                        if ccw { 8 } else { -8 }
                    } else if ccw {
                        count_oct
                    } else {
                        -count_oct
                    };
                    self.emit_octant_arc(radius, start_oct, delta);
                }
                11 if i + 4 < data.len() => {
                    let _start_off = data[i];
                    let _end_off = data[i + 1];
                    let high_r = data[i + 2] as u16;
                    let low_r = data[i + 3] as u16;
                    let spec = data[i + 4] as i8;
                    i += 5;
                    let radius = (((high_r << 8) | low_r) as f64) * self.scale;
                    let ccw = spec >= 0;
                    let abs_spec = spec.unsigned_abs();
                    let start_oct = ((abs_spec >> 4) & 0x07) as i32;
                    let count_oct = (abs_spec & 0x07) as i32;
                    let delta = if count_oct == 0 {
                        if ccw { 8 } else { -8 }
                    } else if ccw {
                        count_oct
                    } else {
                        -count_oct
                    };
                    self.emit_octant_arc(radius, start_oct, delta);
                }
                12 if i + 2 < data.len() => {
                    let dx = (data[i] as i8) as f64 * self.scale;
                    let dy = (data[i + 1] as i8) as f64 * self.scale;
                    let bulge = data[i + 2] as i8;
                    i += 3;
                    self.emit_bulge(dx, dy, bulge);
                }
                13 => {
                    while i + 1 < data.len() && self.steps < max_steps {
                        let dx_b = data[i] as i8;
                        let dy_b = data[i + 1] as i8;
                        i += 2;
                        if dx_b == 0 && dy_b == 0 {
                            break;
                        }
                        if i >= data.len() {
                            break;
                        }
                        let bulge = data[i] as i8;
                        i += 1;
                        self.steps += 1;
                        self.emit_bulge(
                            (dx_b as f64) * self.scale,
                            (dy_b as f64) * self.scale,
                            bulge,
                        );
                    }
                }
                14 => {
                    // Process next command only for vertical text (skip in horizontal layout).
                    self.skip_next = true;
                }
                v if v >= 16 => {
                    let len = ((v >> 4) as f64) * self.scale;
                    let dir = v & 0x0F;
                    let (ux, uy) = match dir {
                        0 => (1.0, 0.0),
                        1 => (1.0, 0.5),
                        2 => (1.0, 1.0),
                        3 => (0.5, 1.0),
                        4 => (0.0, 1.0),
                        5 => (-0.5, 1.0),
                        6 => (-1.0, 1.0),
                        7 => (-1.0, 0.5),
                        8 => (-1.0, 0.0),
                        9 => (-1.0, -0.5),
                        10 => (-1.0, -1.0),
                        11 => (-0.5, -1.0),
                        12 => (0.0, -1.0),
                        13 => (0.5, -1.0),
                        14 => (1.0, -1.0),
                        _ => (1.0, -0.5),
                    };
                    self.emit_line(self.x + ux * len, self.y + uy * len);
                }
                _ => {}
            }
        }
    }
}
