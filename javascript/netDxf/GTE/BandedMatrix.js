// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see LICENSE.BSL-1.0.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;
import { LexicoArray2 } from './LexicoArray2.js';
import { GteInvoke, GteCopyTo, GteHash, GteArray, GteReference, GteRef, GteElementRef, GteFirst, GteLast, GteSortedDictionary } from '../../runtime/GteRuntime.js';


export class BandedMatrix {
  // C# backing state is prefixed with $; public members retain their original names.
  $size = 0;
  $dBand = null;
  $lBands = null;
  $uBands = null;
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    if (args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647)) {
      this.$ctor0(...args);
      return;
    }
    throw new ArgumentException("No matching BandedMatrix constructor. Use BandedMatrix.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  $ctor0(size, numLBands, numUBands) {
    this.$size = size;
    if ((((((size > 0) && (0 <= numLBands)) && (numLBands < size)) && (0 <= numUBands)) && (numUBands < size)))
    {
      this.$dBand = GteArray(size, () => 0);
      let numElements = 0;
      if ((numLBands > 0))
      {
        this.$lBands = GteArray(numLBands, () => null);
        numElements = (size - 1);
        for (let i = 0; (i < numLBands); (i++))
        {
          SetElement(this.$lBands, i, GteArray((numElements--), () => 0));
        }
      }
      if ((numUBands > 0))
      {
        this.$uBands = GteArray(numUBands, () => null);
        numElements = (size - 1);
        for (let i = 0; (i < numUBands); (i++))
        {
          SetElement(this.$uBands, i, GteArray((numElements--), () => 0));
        }
      }
    }
    else
    {
      this.$size = 0;
    }
  }
  static CreateOverload(signature, ...args) {
    if (signature === "int,int,int") {
      if (!(args.length === 3 && (Number.isInteger(args[0]) && args[0] >= -2147483648 && args[0] <= 2147483647) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647) && (Number.isInteger(args[2]) && args[2] >= -2147483648 && args[2] <= 2147483647))) throw new ArgumentException('Arguments do not match the selected constructor.');
      return BandedMatrix.$create0(...args);
    }
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static $create0(...args) { return new BandedMatrix(ConstructorTag, 0, args); }
  get Size() {
    return this.$size;
  }
  get DBand() {
    return this.$dBand;
  }
  get LBands() {
    return this.$lBands;
  }
  get UBands() {
    return this.$uBands;
  }
  get_Item(r, c) {
    if (((((0 <= r) && (r < this.$size)) && (0 <= c)) && (c < this.$size)))
    {
      let band = (c - r);
      if ((band > 0))
      {
        let numUBands = GteReference(this.$uBands).length;
        if ((((--band) < numUBands) && (r < ((this.$size - 1) - band))))
        {
          return GetElement(GetElement(this.$uBands, band), r);
        }
      }
      else
      if ((band < 0))
      {
        band = (-band);
        let numLBands = GteReference(this.$lBands).length;
        if ((((--band) < numLBands) && (c < ((this.$size - 1) - band))))
        {
          return GetElement(GetElement(this.$lBands, band), c);
        }
      }
      else
      {
        return GetElement(this.$dBand, r);
      }
    }
    return 0;
  }
  set_Item(r, c, value) {
    if (((((0 <= r) && (r < this.$size)) && (0 <= c)) && (c < this.$size)))
    {
      let band = (c - r);
      if ((band > 0))
      {
        let numUBands = GteReference(this.$uBands).length;
        if ((((--band) < numUBands) && (r < ((this.$size - 1) - band))))
        {
          SetElement(GetElement(this.$uBands, band), r, value);
        }
      }
      else
      if ((band < 0))
      {
        band = (-band);
        let numLBands = GteReference(this.$lBands).length;
        if ((((--band) < numLBands) && (c < ((this.$size - 1) - band))))
        {
          SetElement(GetElement(this.$lBands, band), c, value);
        }
      }
      else
      {
        SetElement(this.$dBand, r, value);
      }
    }
    else
    {
    }
  }
  CholeskyFactor() {
    if (((GteReference(this.$dBand).length === 0) || (GteReference(this.$lBands).length !== GteReference(this.$uBands).length)))
    {
      return false;
    }
    let sizeM1 = (this.$size - 1);
    let numBands = GteReference(this.$lBands).length;
    for (let i = 0; (i < this.$size); (i++))
    {
      let jMin = (i - numBands);
      if ((jMin < 0))
      {
        jMin = 0;
      }
      let j = 0, k = 0, kMax = 0;
      for (j = jMin; (j < i); (j++))
      {
        kMax = (j + numBands);
        if ((kMax > sizeM1))
        {
          kMax = sizeM1;
        }
        for (k = i; (k <= kMax); (k++))
        {
          this.set_Item(k, i, (this.get_Item(k, i) - MultiplyDouble(this.get_Item(i, j), this.get_Item(k, j))));
        }
      }
      kMax = (j + numBands);
      if ((kMax > sizeM1))
      {
        kMax = sizeM1;
      }
      for (k = 0; (k < i); (k++))
      {
        this.set_Item(k, i, this.get_Item(i, k));
      }
      let diagonal = this.get_Item(i, i);
      if ((diagonal <= 0))
      {
        return false;
      }
      let invSqrt = (1 / DotNetMath.Sqrt(diagonal));
      for (k = i; (k <= kMax); (k++))
      {
        this.set_Item(k, i, (this.get_Item(k, i) * invSqrt));
      }
    }
    return true;
  }
  $SolveSystem0(bVector) {
    return ((GteReference(this).CholeskyFactor() && GteReference(this).$SolveLower0(bVector)) && GteReference(this).$SolveUpper0(bVector));
  }
  $SolveSystem1(bMatrix, numBColumns) {
    return ((GteReference(this).CholeskyFactor() && GteReference(this).$SolveLower1(bMatrix, numBColumns)) && GteReference(this).$SolveUpper1(bMatrix, numBColumns));
  }
  ComputeInverse(inverse) {
    let invA = LexicoArray2.$create0(this.$size, this.$size, inverse);
    let tmpA = this;
    for (let row = 0; (row < this.$size); (row++))
    {
      for (let col = 0; (col < this.$size); (col++))
      {
        if ((row !== col))
        {
          invA.set_Item(row, col, 0);
        }
        else
        {
          invA.set_Item(row, row, 1);
        }
      }
    }
    for (let row = 0; (row < this.$size); (row++))
    {
      let diag = tmpA.get_Item(row, row);
      if ((DotNetMath.Abs(diag) < 5E-324))
      {
        return false;
      }
      let invDiag = (1 / diag);
      tmpA.set_Item(row, row, 1);
      let colMin = (row + 1);
      let colMax = (colMin + GteReference(this.$uBands).length);
      if ((colMax > this.$size))
      {
        colMax = this.$size;
      }
      let c = 0;
      for (c = colMin; (c < colMax); (c++))
      {
        tmpA.set_Item(row, c, (tmpA.get_Item(row, c) * invDiag));
      }
      for (c = 0; (c <= row); (c++))
      {
        invA.set_Item(row, c, (invA.get_Item(row, c) * invDiag));
      }
      let rowMin = (row + 1);
      let rowMax = (rowMin + GteReference(this.$lBands).length);
      if ((rowMax > this.$size))
      {
        rowMax = this.$size;
      }
      for (let r = rowMin; (r < rowMax); (r++))
      {
        let mult = tmpA.get_Item(r, row);
        tmpA.set_Item(r, row, 0);
        for (c = colMin; (c < colMax); (c++))
        {
          tmpA.set_Item(r, c, (tmpA.get_Item(r, c) - MultiplyDouble(mult, tmpA.get_Item(row, c))));
        }
        for (c = 0; (c <= row); (c++))
        {
          invA.set_Item(r, c, (invA.get_Item(r, c) - MultiplyDouble(mult, invA.get_Item(row, c))));
        }
      }
    }
    for (let row = (this.$size - 1); (row >= 1); (row--))
    {
      let rowMax = (row - 1);
      let rowMin = (row - GteReference(this.$uBands).length);
      if ((rowMin < 0))
      {
        rowMin = 0;
      }
      for (let r = rowMax; (r >= rowMin); (r--))
      {
        let mult = tmpA.get_Item(r, row);
        tmpA.set_Item(r, row, 0);
        for (let c = 0; (c < this.$size); (c++))
        {
          invA.set_Item(r, c, (invA.get_Item(r, c) - MultiplyDouble(mult, invA.get_Item(row, c))));
        }
      }
    }
    return true;
  }
  $SolveLower0(dataVector) {
    let dBandSize = GteReference(this.$dBand).length;
    for (let r = 0; (r < dBandSize); (r++))
    {
      let lowerRR = this.get_Item(r, r);
      if ((lowerRR > 0))
      {
        for (let c = 0; (c < r); (c++))
        {
          let lowerRC = this.get_Item(r, c);
          SetElement(dataVector.value, r, (GetElement(dataVector.value, r) - MultiplyDouble(lowerRC, GetElement(dataVector.value, c))));
        }
        SetElement(dataVector.value, r, (GetElement(dataVector.value, r) / lowerRR));
      }
      else
      {
        return false;
      }
    }
    return true;
  }
  $SolveUpper0(dataVector) {
    let dBandSize = GteReference(this.$dBand).length;
    for (let r = (this.$size - 1); (r >= 0); (r--))
    {
      let upperRR = this.get_Item(r, r);
      if ((upperRR > 0))
      {
        for (let c = (r + 1); (c < dBandSize); (c++))
        {
          let upperRC = this.get_Item(r, c);
          SetElement(dataVector.value, r, (GetElement(dataVector.value, r) - MultiplyDouble(upperRC, GetElement(dataVector.value, c))));
        }
        SetElement(dataVector.value, r, (GetElement(dataVector.value, r) / upperRR));
      }
      else
      {
        return false;
      }
    }
    return true;
  }
  $SolveLower1(dataMatrix, numColumns) {
    let data = LexicoArray2.$create0(this.$size, numColumns, dataMatrix.value);
    for (let r = 0; (r < this.$size); (r++))
    {
      let lowerRR = this.get_Item(r, r);
      if ((lowerRR > 0))
      {
        for (let c = 0; (c < r); (c++))
        {
          let lowerRC = this.get_Item(r, c);
          for (let bCol = 0; (bCol < numColumns); (bCol++))
          {
            data.set_Item(r, bCol, (data.get_Item(r, bCol) - MultiplyDouble(lowerRC, data.get_Item(c, bCol))));
          }
        }
        let inverse = (1 / lowerRR);
        for (let bCol = 0; (bCol < numColumns); (bCol++))
        {
          data.set_Item(r, bCol, (data.get_Item(r, bCol) * inverse));
        }
      }
      else
      {
        return false;
      }
    }
    return true;
  }
  $SolveUpper1(dataMatrix, numColumns) {
    let data = LexicoArray2.$create0(this.$size, numColumns, dataMatrix.value);
    for (let r = (this.$size - 1); (r >= 0); (r--))
    {
      let upperRR = this.get_Item(r, r);
      if ((upperRR > 0))
      {
        for (let c = (r + 1); (c < this.$size); (c++))
        {
          let upperRC = this.get_Item(r, c);
          for (let bCol = 0; (bCol < numColumns); (bCol++))
          {
            data.set_Item(r, bCol, (data.get_Item(r, bCol) - MultiplyDouble(upperRC, data.get_Item(c, bCol))));
          }
        }
        let inverse = (1 / upperRR);
        for (let bCol = 0; (bCol < numColumns); (bCol++))
        {
          data.set_Item(r, bCol, (data.get_Item(r, bCol) * inverse));
        }
      }
      else
      {
        return false;
      }
    }
    return true;
  }
  SolveSystem(...args) {
    if (args.length === 2 && (args[0] != null && typeof args[0] === 'object' && 'value' in args[0]) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) return this.$SolveSystem1(...args);
    if (args.length === 1 && (args[0] != null && typeof args[0] === 'object' && 'value' in args[0])) return this.$SolveSystem0(...args);
    throw new ArgumentException("No matching BandedMatrix.SolveSystem overload. Consult native-port-manifest.json.");
  }
  SolveLower(...args) {
    if (args.length === 2 && (args[0] != null && typeof args[0] === 'object' && 'value' in args[0]) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) return this.$SolveLower1(...args);
    if (args.length === 1 && (args[0] != null && typeof args[0] === 'object' && 'value' in args[0])) return this.$SolveLower0(...args);
    throw new ArgumentException("No matching BandedMatrix.SolveLower overload. Consult native-port-manifest.json.");
  }
  SolveUpper(...args) {
    if (args.length === 2 && (args[0] != null && typeof args[0] === 'object' && 'value' in args[0]) && (Number.isInteger(args[1]) && args[1] >= -2147483648 && args[1] <= 2147483647)) return this.$SolveUpper1(...args);
    if (args.length === 1 && (args[0] != null && typeof args[0] === 'object' && 'value' in args[0])) return this.$SolveUpper0(...args);
    throw new ArgumentException("No matching BandedMatrix.SolveUpper overload. Consult native-port-manifest.json.");
  }
}
