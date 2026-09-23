// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { GTE } from './GTE.js';
import { GVector } from './GVector.js';
import { GaussianElimination } from './GaussianElimination.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class GMatrix {
  // C# backing state is prefixed with $; public members retain their original names.
  $numRows = 0;
  $numCols = 0;
  $elements = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (args[2] === null || Array.isArray(args[2]) || args[2] instanceof Float64Array)) {
      this.$ctor2(...args);
      return;
    }
    if (args.length === 4 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647) && (Number.isInteger(args[3]) && args[3] >= -2147483648 && args[3] <= 2147483647)) {
      this.$ctor1(...args);
      return;
    }
    if (args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching GMatrix constructor. Use GMatrix.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(numRows, numCols) {
    this.$numRows = numRows;
    this.$numCols = numCols;
    this.$elements = GVector.$create0((numRows * numCols));
  }
  $ctor1(numRows, numCols, r, c) {
    this.$numRows = numRows;
    this.$numCols = numCols;
    this.$elements = GVector.$create0((numRows * numCols));
    GteReference(this).MakeUnit(r, c);
  }
  $ctor2(numRows, numCols, elements) {
    this.$numRows = numRows;
    this.$numCols = numCols;
    this.$elements = GVector.$create2(elements);
  }
  static CreateOverload(signature, ...args) {
    if (signature === "int,int") {
      if (!(args.length === 2 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return GMatrix.$create0(...args);
    }
    if (signature === "int,int,int,int") {
      if (!(args.length === 4 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647) && (Number.isInteger(args[3]) && args[3] >= -2147483648 && args[3] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return GMatrix.$create1(...args);
    }
    if (signature === "int,int,double[]") {
      if (!(args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (args[2] === null || Array.isArray(args[2]) || args[2] instanceof Float64Array))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return GMatrix.$create2(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new GMatrix(ConstructorTag, 0, args); }
  static $create1(...args) { return new GMatrix(ConstructorTag, 1, args); }
  static $create2(...args) { return new GMatrix(ConstructorTag, 2, args); }
  GetSize(rows, cols) {
    rows.value = this.$numRows;
    cols.value = this.$numCols;
  }
  get NumRows() {
    return this.$numRows;
  }
  get NumCols() {
    return this.$numCols;
  }
  get NumElements() {
    return GteReference(this.$elements).Size;
  }
  get Elements() {
    return this.$elements;
  }
  $get_Item2(r, c) {
    if (((((0 <= r) && (r < GteReference(this).NumRows)) && (0 <= c)) && (c < GteReference(this).NumCols)))
    {
      return (GTE.UseRowMajor ? this.$elements.get_Item((c + (this.$numCols * r))) : this.$elements.get_Item((r + (this.$numRows * c))));
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return DotNetNaN;
  }
  $set_Item2(r, c, value) {
    if (((((0 <= r) && (r < GteReference(this).NumRows)) && (0 <= c)) && (c < GteReference(this).NumCols)))
    {
      if (GTE.UseRowMajor)
      {
        this.$elements.set_Item((c + (this.$numCols * r)), value);
      }
      else
      {
        this.$elements.set_Item((r + (this.$numRows * c)), value);
      }
    }
    else
    {
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    }
  }
  SetRow(r, vec) {
    if (((0 <= r) && (r < this.$numRows)))
    {
      if ((GteReference(vec).Size === GteReference(this).NumCols))
      {
        for (let c = 0; (c < this.$numCols); (++c))
        {
          this.set_Item(r, c, vec.get_Item(c));
        }
        return;
      }
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
  }
  SetCol(c, vec) {
    if (((0 <= c) && (c < this.$numCols)))
    {
      if ((GteReference(vec).Size === GteReference(this).NumRows))
      {
        for (let r = 0; (r < this.$numRows); (++r))
        {
          this.set_Item(r, c, vec.get_Item(r));
        }
        return;
      }
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
  }
  GetRow(r) {
    if (((0 <= r) && (r < this.$numRows)))
    {
      let vec = GVector.$create0(this.$numCols);
      for (let c = 0; (c < this.$numCols); (++c))
      {
        vec.set_Item(c, this.get_Item(r, c));
      }
      return vec;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GVector.$create0(0);
  }
  GetCol(c) {
    if (((0 <= c) && (c < this.$numCols)))
    {
      let vec = GVector.$create0(this.$numRows);
      for (let r = 0; (r < this.$numRows); (++r))
      {
        vec.set_Item(r, this.get_Item(r, c));
      }
      return vec;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GVector.$create0(0);
  }
  $get_Item1(i) {
    return this.$elements.get_Item(i);
  }
  $set_Item1(i, value) {
    this.$elements.set_Item(i, value);
  }
  static op_Equality(mat1, mat2) {
    if ((GMatrix.op_Equality(mat1, null) || GMatrix.op_Equality(mat2, null)))
    {
      return false;
    }
    return (((mat1.$numRows === mat2.$numRows) && (mat1.$numCols === mat2.$numCols)) && GVector.op_Equality(mat1.$elements, mat2.$elements));
  }
  static op_Inequality(mat1, mat2) {
    if ((GMatrix.op_Equality(mat1, null) || GMatrix.op_Equality(mat2, null)))
    {
      return false;
    }
    return (((mat1.$numRows === mat2.$numRows) && (mat1.$numCols === mat2.$numCols)) && GVector.op_Inequality(mat1.$elements, mat2.$elements));
  }
  static op_LessThan(mat1, mat2) {
    return (((mat1.$numRows === mat2.$numRows) && (mat1.$numCols === mat2.$numCols)) && GVector.op_LessThan(mat1.$elements, mat2.$elements));
  }
  static op_LessThanOrEqual(mat1, mat2) {
    return (((mat1.$numRows === mat2.$numRows) && (mat1.$numCols === mat2.$numCols)) && GVector.op_LessThanOrEqual(mat1.$elements, mat2.$elements));
  }
  static op_GreaterThan(mat1, mat2) {
    return (((mat1.$numRows === mat2.$numRows) && (mat1.$numCols === mat2.$numCols)) && GVector.op_GreaterThan(mat1.$elements, mat2.$elements));
  }
  static op_GreaterThanOrEqual(mat1, mat2) {
    return (((mat1.$numRows === mat2.$numRows) && (mat1.$numCols === mat2.$numCols)) && GVector.op_GreaterThanOrEqual(mat1.$elements, mat2.$elements));
  }
  MakeZero() {
    GteReference(this.$elements).MakeZero();
  }
  MakeUnit(r, c) {
    if (((((0 <= r) && (r < this.$numRows)) && (0 <= c)) && (c < this.$numCols)))
    {
      GteReference(this).MakeZero();
      this.set_Item(r, c, 1);
      return;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
  }
  MakeIdentity() {
    GteReference(this).MakeZero();
    let numDiagonal = ((this.$numRows <= this.$numCols) ? this.$numRows : this.$numCols);
    for (let i = 0; (i < numDiagonal); (++i))
    {
      this.set_Item(i, i, 1);
    }
  }
  static Zero(numRows, numCols) {
    let M = GMatrix.$create0(numRows, numCols);
    GteReference(M).MakeZero();
    return M;
  }
  static Unit(numRows, numCols, r, c) {
    let M = GMatrix.$create0(numRows, numCols);
    GteReference(M).MakeUnit(r, c);
    return M;
  }
  static Identity(numRows, numCols) {
    let M = GMatrix.$create0(numRows, numCols);
    GteReference(M).MakeIdentity();
    return M;
  }
  static op_Addition(m1, m2) {
    if (((GteReference(m1).NumRows === GteReference(m2).NumRows) && (GteReference(m1).NumCols === GteReference(m2).NumCols)))
    {
      let result = GMatrix.$create0(m1.$numRows, m1.$numCols);
      for (let i = 0; (i < GteReference(result).NumElements); (i++))
      {
        result.set_Item(i, (m1.$elements.get_Item(i) + m2.$elements.get_Item(i)));
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static op_Subtraction(m1, m2) {
    if (((GteReference(m1).NumRows === GteReference(m2).NumRows) && (GteReference(m1).NumCols === GteReference(m2).NumCols)))
    {
      let result = GMatrix.$create0(m1.$numRows, m1.$numCols);
      for (let i = 0; (i < GteReference(result).NumElements); (i++))
      {
        result.set_Item(i, (m1.$elements.get_Item(i) - m2.$elements.get_Item(i)));
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static $op_Multiply0(scalar, m) {
    let result = GMatrix.$create0(m.$numRows, m.$numCols);
    for (let i = 0; (i < GteReference(result).NumElements); (i++))
    {
      result.set_Item(i, MultiplyDouble(scalar, m.$elements.get_Item(i)));
    }
    return result;
  }
  static $op_Multiply1(m, scalar) {
    return GMatrix.$op_Multiply0(scalar, m);
  }
  static op_Division(m, scalar) {
    return GMatrix.$op_Multiply1(m, ((1 / scalar)));
  }
  static L1Norm(m) {
    let sum = 0;
    for (let i = 0; (i < GteReference(m).NumElements); (i++))
    {
      sum += DotNetMath.Abs(m.get_Item(i));
    }
    return sum;
  }
  static L2Norm(m) {
    let sum = 0;
    for (let i = 0; (i < GteReference(m).NumElements); (i++))
    {
      sum += MultiplyDouble(m.get_Item(i), m.get_Item(i));
    }
    return DotNetMath.Sqrt(sum);
  }
  static LInfinityNorm(m) {
    let maxAbsElement = 0;
    for (let i = 0; (i < GteReference(m).NumElements); (i++))
    {
      let absElement = DotNetMath.Abs(m.get_Item(i));
      if ((absElement > maxAbsElement))
      {
        maxAbsElement = absElement;
      }
    }
    return maxAbsElement;
  }
  static Inverse(m, invertible) {
    invertible.value = false;
    if ((GteReference(m).NumRows === GteReference(m).NumCols))
    {
      let invM = GteArray((GteReference(m).NumRows * GteReference(m).NumCols), () => 0);
      invertible.value = GaussianElimination.Solve(GteReference(m).NumRows, GteReference(m.$elements).Vector, invM, { value: 0 }, null, null, null, 0, null);
      return GMatrix.$create2(GteReference(m).NumRows, GteReference(m).NumCols, invM);
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static Determinant(m) {
    let determinant;
    if ((GteReference(m).NumRows === GteReference(m).NumCols))
    {
      GaussianElimination.Solve(GteReference(m).NumRows, GteReference(m.$elements).Vector, null, GteRef(() => determinant, v => { determinant = v; }), null, null, null, 0, null);
      return determinant;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return DotNetNaN;
  }
  static Transpose(m) {
    let result = GMatrix.$create0(GteReference(m).NumCols, GteReference(m).NumRows);
    for (let r = 0; (r < GteReference(m).NumRows); (++r))
    {
      for (let c = 0; (c < GteReference(m).NumCols); (++c))
      {
        result.set_Item(c, r, m.get_Item(r, c));
      }
    }
    return result;
  }
  static $op_Multiply2(m, v) {
    if ((GteReference(v).Size === GteReference(m).NumCols))
    {
      let result = GVector.$create0(GteReference(m).NumRows);
      for (let r = 0; (r < GteReference(m).NumRows); (++r))
      {
        result.set_Item(r, 0);
        for (let c = 0; (c < GteReference(m).NumCols); (++c))
        {
          result.set_Item(r, (result.get_Item(r) + MultiplyDouble(m.get_Item(r, c), v.get_Item(c))));
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GVector.$create0(0);
  }
  static $op_Multiply3(v, m) {
    if ((GteReference(v).Size === GteReference(m).NumRows))
    {
      let result = GVector.$create0(GteReference(m).NumCols);
      for (let c = 0; (c < GteReference(m).NumCols); (++c))
      {
        result.set_Item(c, 0);
        for (let r = 0; (r < GteReference(m).NumRows); (++r))
        {
          result.set_Item(c, (result.get_Item(c) + MultiplyDouble(v.get_Item(r), m.get_Item(r, c))));
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GVector.$create0(0);
  }
  static $op_Multiply4(a, b) {
    return GMatrix.MultiplyAB(a, b);
  }
  static MultiplyAB(a, b) {
    if ((GteReference(a).NumCols === GteReference(b).NumRows))
    {
      let result = GMatrix.$create0(GteReference(a).NumRows, GteReference(b).NumCols);
      let numCommon = GteReference(a).NumCols;
      for (let r = 0; (r < GteReference(result).NumRows); (++r))
      {
        for (let c = 0; (c < GteReference(result).NumCols); (++c))
        {
          result.set_Item(r, c, 0);
          for (let i = 0; (i < numCommon); (++i))
          {
            result.set_Item(r, c, (result.get_Item(r, c) + MultiplyDouble(a.get_Item(r, i), b.get_Item(i, c))));
          }
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static MultiplyABT(a, b) {
    if ((GteReference(a).NumCols === GteReference(b).NumCols))
    {
      let result = GMatrix.$create0(GteReference(a).NumRows, GteReference(b).NumRows);
      let numCommon = GteReference(a).NumCols;
      for (let r = 0; (r < GteReference(result).NumRows); (++r))
      {
        for (let c = 0; (c < GteReference(result).NumCols); (++c))
        {
          result.set_Item(r, c, 0);
          for (let i = 0; (i < numCommon); (++i))
          {
            result.set_Item(r, c, (result.get_Item(r, c) + MultiplyDouble(a.get_Item(r, i), b.get_Item(c, i))));
          }
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static MultiplyATB(a, b) {
    if ((GteReference(a).NumRows === GteReference(b).NumRows))
    {
      let result = GMatrix.$create0(GteReference(a).NumCols, GteReference(b).NumCols);
      let numCommon = GteReference(a).NumRows;
      for (let r = 0; (r < GteReference(result).NumRows); (++r))
      {
        for (let c = 0; (c < GteReference(result).NumCols); (++c))
        {
          result.set_Item(r, c, 0);
          for (let i = 0; (i < numCommon); (++i))
          {
            result.set_Item(r, c, (result.get_Item(r, c) + MultiplyDouble(a.get_Item(i, r), b.get_Item(i, c))));
          }
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static MultiplyATBT(a, b) {
    if ((GteReference(a).NumRows === GteReference(b).NumCols))
    {
      let result = GMatrix.$create0(GteReference(a).NumCols, GteReference(b).NumRows);
      let numCommon = GteReference(a).NumRows;
      for (let r = 0; (r < GteReference(result).NumRows); (++r))
      {
        for (let c = 0; (c < GteReference(result).NumCols); (++c))
        {
          result.set_Item(r, c, 0);
          for (let i = 0; (i < numCommon); (++i))
          {
            result.set_Item(r, c, (result.get_Item(r, c) + MultiplyDouble(a.get_Item(i, r), b.get_Item(c, i))));
          }
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static MultiplyMD(m, d) {
    if ((GteReference(d).Size === GteReference(m).NumCols))
    {
      let result = GMatrix.$create0(GteReference(m).NumRows, GteReference(m).NumCols);
      for (let r = 0; (r < GteReference(result).NumRows); (++r))
      {
        for (let c = 0; (c < GteReference(result).NumCols); (++c))
        {
          result.set_Item(r, c, MultiplyDouble(m.get_Item(r, c), d.get_Item(c)));
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static MultiplyDM(d, m) {
    if ((GteReference(d).Size === GteReference(m).NumRows))
    {
      let result = GMatrix.$create0(GteReference(m).NumRows, GteReference(m).NumCols);
      for (let r = 0; (r < GteReference(result).NumRows); (++r))
      {
        for (let c = 0; (c < GteReference(result).NumCols); (++c))
        {
          result.set_Item(r, c, MultiplyDouble(d.get_Item(r), m.get_Item(r, c)));
        }
      }
      return result;
    }
    // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    return GMatrix.$create0(0, 0);
  }
  static OuterProduct(u, v) {
    let result = GMatrix.$create0(GteReference(u).Size, GteReference(v).Size);
    for (let r = 0; (r < GteReference(result).NumRows); (++r))
    {
      for (let c = 0; (c < GteReference(result).NumCols); (++c))
      {
        result.set_Item(r, c, MultiplyDouble(u.get_Item(r), v.get_Item(c)));
      }
    }
    return result;
  }
  static MakeDiagonal(d, m) {
    let numRows = GteReference(m).NumRows;
    let numCols = GteReference(m).NumCols;
    let numDiagonal = ((numRows <= numCols) ? numRows : numCols);
    GteReference(m).MakeZero();
    for (let i = 0; (i < numDiagonal); (++i))
    {
      m.set_Item(i, i, d.get_Item(i));
    }
  }
  $Equals0(other) {
    if (GMatrix.op_Equality(other, null))
    {
      return false;
    }
    return GMatrix.op_Equality(this, other);
  }
  $Equals1(obj) {
    if ((obj === null))
    {
      return false;
    }
    return ((GteReference(obj).constructor === GteReference(this).constructor) && GteReference(this).$Equals0(obj));
  }
  GetHashCode() {
    return GteReference(this.$elements).GetHashCode();
  }
  get_Item(...args) {
    if (args.length === 2) return this.$get_Item2(...args);
    if (args.length === 1) return this.$get_Item1(...args);
    throw new ArgumentException('No matching indexer overload.');
  }
  set_Item(...args) {
    if (args.length === 3) return this.$set_Item2(...args);
    if (args.length === 2) return this.$set_Item1(...args);
    throw new ArgumentException('No matching indexer overload.');
  }
  static op_Multiply(...args) {
    if (args.length === 2 && (args[0] === null || args[0] instanceof GMatrix) && (args[1] === null || args[1] instanceof GVector)) return GMatrix.$op_Multiply2(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof GVector) && (args[1] === null || args[1] instanceof GMatrix)) return GMatrix.$op_Multiply3(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof GMatrix) && (args[1] === null || args[1] instanceof GMatrix)) return GMatrix.$op_Multiply4(...args);
    if (args.length === 2 && (typeof args[0] === 'number') && (args[1] === null || args[1] instanceof GMatrix)) return GMatrix.$op_Multiply0(...args);
    if (args.length === 2 && (args[0] === null || args[0] instanceof GMatrix) && (typeof args[1] === 'number')) return GMatrix.$op_Multiply1(...args);
    throw new ArgumentException("No matching GMatrix.op_Multiply overload. Consult native-port-manifest.json.");
  }
  Equals(...args) {
    if (args.length === 1 && (args[0] === null || args[0] instanceof GMatrix)) return this.$Equals0(...args);
    if (args.length === 1 && (true)) return this.$Equals1(...args);
    throw new ArgumentException("No matching GMatrix.Equals overload. Consult native-port-manifest.json.");
  }
}
