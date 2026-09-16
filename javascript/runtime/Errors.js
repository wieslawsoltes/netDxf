/** .NET exception names retained for mapped JavaScript argument and IO failures. */
export class Exception extends Error {
  constructor(message = '', options) { super(message, options); this.name = new.target.name; }
}
export class ArgumentException extends Exception {
  constructor(message = 'Invalid argument.', paramName = null) {
    super(message); this.ParamName = paramName;
  }
}
export class ArgumentNullException extends ArgumentException {
  constructor(paramName) { super('Value cannot be null.', paramName); }
}
export class ArgumentOutOfRangeException extends ArgumentException {
  constructor(paramName, actualValue, message = 'Argument is out of range.') {
    super(message, paramName); this.ActualValue = actualValue;
  }
}
export class OverflowException extends Exception {}
export class KeyNotFoundException extends Exception {}
export class IOException extends Exception {}
export class InvalidCastException extends Exception {}
export class NullReferenceException extends Exception {}
export class FormatException extends Exception {}
export class InvalidDataException extends Exception {}
export class EndOfStreamException extends Exception {}
export class NotSupportedException extends Exception {}
export class InvalidOperationException extends Exception {}
export class OperationCanceledException extends Exception {}
export class ObjectDisposedException extends InvalidOperationException {}
export class EncoderFallbackException extends ArgumentException {}
export class DecoderFallbackException extends ArgumentException {}
export function ThrowIfCancellationRequested(token) {
  if (token == null) return;
  if (typeof token.ThrowIfCancellationRequested === 'function') token.ThrowIfCancellationRequested();
  else if (token.aborted) throw new OperationCanceledException('The operation was canceled.');
}
export function RequireInteger(value, minimum, maximum, name = 'value') {
  if (!Number.isInteger(value) || value < minimum || value > maximum)
    throw new ArgumentOutOfRangeException(name, value);
  return value === 0 ? 0 : value;
}
