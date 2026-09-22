// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { HeaderDateTime, HeaderTimeSpan } from '../../runtime/HeaderTime.js';
import { Int32 } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';

// System.DateTime's Gregorian component constructor, limited to the overload
// used below. All arithmetic after calendar validation is integral, in ticks.
function calendar(year, month, day, hour, minute, second, millisecond) {
  if (millisecond < 0 || millisecond >= 1000) throw new ArgumentOutOfRangeException('millisecond', millisecond);
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [0,31,leap ? 60 : 59,leap ? 91 : 90,leap ? 121 : 120,leap ? 152 : 151,
    leap ? 182 : 181,leap ? 213 : 212,leap ? 244 : 243,leap ? 274 : 273,leap ? 305 : 304,leap ? 335 : 334,leap ? 366 : 365];
  if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > days[month] - days[month - 1])
    throw new ArgumentOutOfRangeException(null, null, 'Year, Month, and Day parameters describe an un-representable DateTime.');
  if (hour < 0 || hour >= 24 || minute < 0 || minute >= 60 || second < 0 || second >= 60)
    throw new ArgumentOutOfRangeException(null, null, 'Hour, Minute, and Second parameters describe an un-representable DateTime.');
  const y = year - 1, totalDays = y * 365 + Math.trunc(y / 4) - Math.trunc(y / 100) + Math.trunc(y / 400) + days[month - 1] + day - 1;
  return new HeaderDateTime(BigInt(totalDays) * 864000000000n + BigInt(hour * 3600 + minute * 60 + second) * 10000000n + BigInt(millisecond) * 10000n);
}

/** Source arithmetic for DXF Julian timestamps and fractional-day editing durations.
 * DateTime/TimeSpan arguments and results use the package's exact tick adapters.
 * Millisecond truncation, range checks and unchecked Int32 casts match the source;
 * this is not a higher-precision alternative calendar conversion.
 */
export class DrawingTime {
  static ToJulianCalendar(date) {
    if (!(date instanceof HeaderDateTime)) throw new ArgumentException('A HeaderDateTime value is required.', 'date');
    let year = date.Year, month = date.Month;
    const fraction = date.Day + date.Hour / 24.0 + date.Minute / 1440.0 + (date.Second + date.Millisecond / 1000) / 86400.0;
    if (month < 3) { year--; month += 12; }
    const a = Int32(year / 100), b = 2 - a + Int32(a / 4);
    const c = Int32(year < 0 ? 365.25 * year - 0.75 : 365.25 * year);
    const d = Int32(30.6001 * (month + 1));
    return b + c + d + 1720995 + fraction;
  }
  static FromJulianCalendar(date) {
    if (date < 1721426 || date > 5373484) throw new ArgumentOutOfRangeException('date', date);
    let julian = Int32(date), fraction = date - julian;
    const temp = Int32((julian - 1867216.25) / 36524.25);
    julian = julian + 1 + temp - Int32(temp / 4.0);
    const a = (Int32(julian) + 1524) | 0, b = Int32((a - 122.1) / 365.25), c = Int32(365.25 * b);
    const d = Int32(((a - c) | 0) / 30.6001);
    const month = d < 14 ? (d - 1) | 0 : (d - 13) | 0;
    const year = month > 2 ? (b - 4716) | 0 : (b - 4715) | 0;
    const day = (a - c - Int32(30.6001 * d)) | 0;
    const hour = Int32(fraction * 24); fraction -= hour / 24.0;
    const minute = Int32(fraction * 1440); fraction -= minute / 1440.0;
    const decimalSeconds = fraction * 86400, second = Int32(decimalSeconds);
    const millisecond = Int32((decimalSeconds - second) * 1000);
    return calendar(year, month, day, hour, minute, second, millisecond);
  }
  static EditingTime(elapsed) {
    const days = Int32(elapsed); let fraction = elapsed - days;
    const hours = Int32(fraction * 24); fraction -= hours / 24.0;
    const minutes = Int32(fraction * 1440); fraction -= minutes / 1440.0;
    const decimalSeconds = fraction * 86400, seconds = Int32(decimalSeconds);
    const milliseconds = Int32((decimalSeconds - seconds) * 1000);
    const total = ((BigInt(days) * 86400n + BigInt(hours) * 3600n + BigInt(minutes) * 60n + BigInt(seconds)) * 1000n + BigInt(milliseconds));
    if (total > 922337203685477n || total < -922337203685477n) throw new ArgumentOutOfRangeException(null, null, 'TimeSpan overflowed because the duration is too long.');
    return new HeaderTimeSpan(total * 10000n);
  }
}
