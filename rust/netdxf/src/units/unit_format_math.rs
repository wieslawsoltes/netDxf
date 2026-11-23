use crate::error::{DxfError, Result};
use crate::units::UnitStyleFormat;
use std::cmp::Ordering;

/// Minimal exact arbitrary-precision unsigned integer (base 2^32) for exact binary64 rounding.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct BigUint {
    limbs: Vec<u32>,
}

impl BigUint {
    pub(crate) fn zero() -> Self {
        Self { limbs: Vec::new() }
    }

    pub(crate) fn from_u64(v: u64) -> Self {
        let mut limbs = Vec::new();
        if v != 0 {
            limbs.push(v as u32);
            let hi = (v >> 32) as u32;
            if hi != 0 {
                limbs.push(hi);
            }
        }
        Self { limbs }
    }

    pub(crate) fn is_zero(&self) -> bool {
        self.limbs.is_empty()
    }

    pub(crate) fn is_even(&self) -> bool {
        self.limbs.first().map_or(true, |&w| (w & 1) == 0)
    }

    fn normalize(&mut self) {
        while self.limbs.last() == Some(&0) {
            self.limbs.pop();
        }
    }

    pub(crate) fn add_u32(&mut self, mut carry: u32) {
        for w in &mut self.limbs {
            let sum = (*w as u64) + (carry as u64);
            *w = sum as u32;
            carry = (sum >> 32) as u32;
            if carry == 0 {
                return;
            }
        }
        if carry != 0 {
            self.limbs.push(carry);
        }
    }

    pub(crate) fn mul_u64(&self, factor: u64) -> Self {
        if self.is_zero() || factor == 0 {
            return Self::zero();
        }
        let lo = factor as u32 as u64;
        let hi = (factor >> 32) as u32 as u64;
        let mut out = vec![0u32; self.limbs.len() + 2];
        for (i, &w) in self.limbs.iter().enumerate() {
            let w64 = w as u64;
            let p_lo = w64 * lo;
            let mut carry = (out[i] as u64) + (p_lo & 0xFFFF_FFFF);
            out[i] = carry as u32;
            carry >>= 32;

            let p_hi = w64 * hi;
            carry += (out[i + 1] as u64) + (p_lo >> 32) + (p_hi & 0xFFFF_FFFF);
            out[i + 1] = carry as u32;
            carry >>= 32;

            carry += (out[i + 2] as u64) + (p_hi >> 32);
            out[i + 2] = carry as u32;
            let mut j = i + 3;
            let mut rem = carry >> 32;
            while rem != 0 {
                if j >= out.len() {
                    out.push(0);
                }
                let s = (out[j] as u64) + rem;
                out[j] = s as u32;
                rem = s >> 32;
                j += 1;
            }
        }
        let mut res = Self { limbs: out };
        res.normalize();
        res
    }

    pub(crate) fn shl(&self, bits: usize) -> Self {
        if self.is_zero() || bits == 0 {
            return self.clone();
        }
        let word_shift = bits / 32;
        let bit_shift = bits % 32;
        let mut out = vec![0u32; self.limbs.len() + word_shift + 1];
        if bit_shift == 0 {
            for (i, &w) in self.limbs.iter().enumerate() {
                out[i + word_shift] = w;
            }
        } else {
            let mut carry = 0u32;
            for (i, &w) in self.limbs.iter().enumerate() {
                out[i + word_shift] = (w << bit_shift) | carry;
                carry = w >> (32 - bit_shift);
            }
            if carry != 0 {
                out[self.limbs.len() + word_shift] = carry;
            }
        }
        let mut res = Self { limbs: out };
        res.normalize();
        res
    }

    pub(crate) fn shr_with_round_to_even(&self, bits: usize) -> Self {
        if self.is_zero() || bits == 0 {
            return self.clone();
        }
        let word_shift = bits / 32;
        let bit_shift = bits % 32;
        let total_bits = self.bit_len();
        if bits > total_bits {
            return Self::zero();
        }
        // Quotient = self >> bits
        let mut q_limbs = Vec::new();
        if word_shift < self.limbs.len() {
            if bit_shift == 0 {
                q_limbs.extend_from_slice(&self.limbs[word_shift..]);
            } else {
                for i in word_shift..self.limbs.len() {
                    let lo = self.limbs[i] >> bit_shift;
                    let hi = if i + 1 < self.limbs.len() {
                        self.limbs[i + 1] << (32 - bit_shift)
                    } else {
                        0
                    };
                    q_limbs.push(lo | hi);
                }
            }
        }
        let mut q = Self { limbs: q_limbs };
        q.normalize();

        // Check remainder against 2^(bits - 1)
        let half_bit = bits - 1;
        let half_is_set = self.get_bit(half_bit);
        let any_lower_set = self.has_any_bit_below(half_bit);
        if half_is_set && (any_lower_set || !q.is_even()) {
            q.add_u32(1);
        }
        q
    }

    fn bit_len(&self) -> usize {
        match self.limbs.last() {
            None => 0,
            Some(&top) => (self.limbs.len() - 1) * 32 + (32 - top.leading_zeros() as usize),
        }
    }

    fn get_bit(&self, bit: usize) -> bool {
        let w = bit / 32;
        let b = bit % 32;
        self.limbs.get(w).is_some_and(|&v| ((v >> b) & 1) != 0)
    }

    fn has_any_bit_below(&self, bit: usize) -> bool {
        let w = bit / 32;
        let b = bit % 32;
        for i in 0..w.min(self.limbs.len()) {
            if self.limbs[i] != 0 {
                return true;
            }
        }
        if b > 0 && w < self.limbs.len() {
            let mask = (1u32 << b) - 1;
            if (self.limbs[w] & mask) != 0 {
                return true;
            }
        }
        false
    }

    pub(crate) fn div_rem_u64(&self, divisor: u64) -> (Self, u64) {
        debug_assert!(divisor > 0);
        if self.is_zero() {
            return (Self::zero(), 0);
        }
        let mut rem: u128 = 0;
        let d = divisor as u128;
        let mut q = vec![0u32; self.limbs.len()];
        for i in (0..self.limbs.len()).rev() {
            let cur = (rem << 32) | (self.limbs[i] as u128);
            q[i] = (cur / d) as u32;
            rem = cur % d;
        }
        let mut res = Self { limbs: q };
        res.normalize();
        (res, rem as u64)
    }

    pub(crate) fn to_decimal_string(&self) -> String {
        if self.is_zero() {
            return "0".to_string();
        }
        let mut cur = self.clone();
        let mut chunks = Vec::new();
        while !cur.is_zero() {
            let (q, r) = cur.div_rem_u64(1_000_000_000);
            chunks.push(r as u32);
            cur = q;
        }
        let mut out = String::new();
        if let Some(&last) = chunks.last() {
            out.push_str(&last.to_string());
        }
        for &c in chunks.iter().rev().skip(1) {
            out.push_str(&format!("{c:09}"));
        }
        out
    }
}

impl Ord for BigUint {
    fn cmp(&self, other: &Self) -> Ordering {
        self.limbs.len().cmp(&other.limbs.len()).then_with(|| {
            for (a, b) in self.limbs.iter().rev().zip(other.limbs.iter().rev()) {
                match a.cmp(b) {
                    Ordering::Equal => continue,
                    non_eq => return non_eq,
                }
            }
            Ordering::Equal
        })
    }
}

impl PartialOrd for BigUint {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

pub(crate) fn check_finite(value: f64, parameter: &str) -> Result<()> {
    if !value.is_finite() {
        return Err(DxfError::InvalidValue(format!(
            "The value for '{parameter}' must be finite."
        )));
    }
    Ok(())
}

pub(crate) fn validate(value: f64, format: &UnitStyleFormat, angular: bool) -> Result<usize> {
    check_finite(value, "value")?;
    let places = if angular {
        format.angular_decimal_places
    } else {
        format.linear_decimal_places
    };
    if !(0..=8).contains(&places) {
        return Err(DxfError::InvalidValue(
            "DXF display precision must be between zero and eight.".into(),
        ));
    }
    if format.decimal_separator.is_empty() {
        return Err(DxfError::InvalidValue(
            "A nonempty decimal separator is required.".into(),
        ));
    }
    Ok(places as usize)
}

pub(crate) fn round_magnitude(value: f64, scale: u64) -> BigUint {
    let bits = value.abs().to_bits();
    let exponent = ((bits >> 52) & 0x7ff) as i32;
    let mut mantissa = bits & 0x000f_ffff_ffff_ffff;
    if exponent != 0 {
        mantissa |= 1u64 << 52;
    }
    if mantissa == 0 {
        return BigUint::zero();
    }
    let shift = if exponent == 0 {
        -1074
    } else {
        exponent - 1075
    };
    let numerator = BigUint::from_u64(mantissa).mul_u64(scale);
    if shift >= 0 {
        numerator.shl(shift as usize)
    } else {
        numerator.shr_with_round_to_even((-shift) as usize)
    }
}

pub(crate) fn decimal(
    value: &BigUint,
    places: usize,
    separator: &str,
    suppress_leading: bool,
    suppress_trailing: bool,
) -> String {
    let raw = value.to_decimal_string();
    let padded = if raw.len() <= places {
        let mut s = "0".repeat(places + 1 - raw.len());
        s.push_str(&raw);
        s
    } else {
        raw
    };
    let split = padded.len() - places;
    let mut whole = &padded[..split];
    let mut fraction = if places == 0 { "" } else { &padded[split..] };
    if suppress_trailing {
        fraction = fraction.trim_end_matches('0');
    }
    if suppress_leading && whole == "0" && !fraction.is_empty() {
        whole = "";
    }
    if fraction.is_empty() {
        whole.to_string()
    } else {
        format!("{whole}{separator}{fraction}")
    }
}

pub(crate) fn fixed(
    value: f64,
    places: usize,
    separator: &str,
    suppress_leading: bool,
    suppress_trailing: bool,
) -> Result<String> {
    check_finite(value, "value")?;
    let scale = 10u64.pow(places as u32);
    let rounded = round_magnitude(value, scale);
    let sign = if value < 0.0 && !rounded.is_zero() {
        "-"
    } else {
        ""
    };
    Ok(format!(
        "{sign}{}",
        decimal(&rounded, places, separator, suppress_leading, suppress_trailing)
    ))
}

pub(crate) fn gcd_u64(mut a: u64, mut b: u64) -> u64 {
    while b != 0 {
        let t = a % b;
        a = b;
        b = t;
    }
    a
}
