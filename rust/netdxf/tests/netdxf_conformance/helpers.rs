use std::io::Read;

use netdxf::entities::Spline;
use netdxf::header::DxfVersion;
use netdxf::io::BINARY_DXF_SENTINEL;
use netdxf::{Vector3, Vector4};

pub const SUPPORTED_VERSIONS: [DxfVersion; 6] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
    DxfVersion::AutoCad2013,
    DxfVersion::AutoCad2018,
];

pub const SAT_VERSIONS: [DxfVersion; 4] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
];

/// Exact binary64 power-of-two scaling (handles subnormals down to 2^-1074).
pub fn scale_b(value: f64, exp: i32) -> f64 {
    let mut v = value;
    let mut e = exp;
    while e > 1023 {
        v *= f64::from_bits(0x7fe0_0000_0000_0000);
        e -= 1023;
    }
    while e < -1022 {
        v *= f64::from_bits(0x0010_0000_0000_0000);
        e += 1022;
    }
    let scale = f64::from_bits(((e + 1023) as u64) << 52);
    v * scale
}

pub fn vec3_bits(v: Vector3) -> [u64; 3] {
    [v.x.to_bits(), v.y.to_bits(), v.z.to_bits()]
}

pub fn vec4_bits(v: Vector4) -> [u64; 4] {
    [v.x.to_bits(), v.y.to_bits(), v.z.to_bits(), v.w.to_bits()]
}

pub fn make_rational_spline(degree: i16) -> Spline {
    let d = degree as usize;
    let count = d + 4;
    let controls: Vec<Vector3> = (0..count)
        .map(|i| {
            Vector3::new(
                i as f64 - 2.0,
                ((i * 7) % 11) as f64 - 4.0,
                (i % 3) as f64 - 1.0,
            )
        })
        .collect();
    let weights: Vec<f64> = (0..count).map(|i| 1.0 + (i % 4) as f64 * 0.25).collect();
    let knot_count = count + d + 1;
    let knots: Vec<f64> = (0..knot_count)
        .map(|i| ((i as f64 - d as f64) / 4.0).clamp(0.0, 1.0))
        .collect();
    let mut s = Spline::from_control_points(controls, degree, false);
    s.weights = weights;
    s.knots = knots;
    s
}

pub fn make_rational_bezier(degree: i16) -> Spline {
    let d = degree as usize;
    let count = d + 1;
    let controls: Vec<Vector3> = (0..count)
        .map(|i| Vector3::new(i as f64, ((i * 5) % 7) as f64 - 2.0, (i % 3) as f64))
        .collect();
    let weights: Vec<f64> = (0..count).map(|i| 1.0 + (i % 3) as f64 * 0.5).collect();
    let mut s = Spline::from_control_points(controls, degree, false);
    s.weights = weights;
    s
}

pub struct OneByteReader<'a> {
    pub data: &'a [u8],
    pub pos: usize,
}

impl<'a> Read for OneByteReader<'a> {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        if self.pos >= self.data.len() || buf.is_empty() {
            return Ok(0);
        }
        buf[0] = self.data[self.pos];
        self.pos += 1;
        Ok(1)
    }
}

pub fn binary_tag_bytes(code: i16, payload: &[u8], with_eof: bool) -> Vec<u8> {
    let mut out = Vec::new();
    out.extend_from_slice(BINARY_DXF_SENTINEL);
    out.extend_from_slice(&code.to_le_bytes());
    out.extend_from_slice(payload);
    if with_eof {
        out.extend_from_slice(&0i16.to_le_bytes());
        out.extend_from_slice(b"EOF\0");
    }
    out
}
