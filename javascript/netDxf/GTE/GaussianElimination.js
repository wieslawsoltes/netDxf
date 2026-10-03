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


export class GaussianElimination {
  // C# backing state is prefixed with $; public members retain their original names.
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching GaussianElimination constructor. Use GaussianElimination.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  static Solve(numRows, M, inverseM, determinant, B, X, C, numCols, Y) {
    if ((((((numRows <= 0) || (M === null)) || ((((B !== null)) !== ((X !== null))))) || ((((C !== null)) !== ((Y !== null))))) || (((C !== null) && (numCols < 1)))))
    {
      // Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.
    }
    let numElements = (numRows * numRows);
    let wantInverse = (inverseM !== null);
    GaussianElimination.Set(numElements, M, GteRef(() => inverseM, v => { inverseM = v; }));
    if ((B !== null))
    {
      GaussianElimination.Set(numRows, B, GteRef(() => X, v => { X = v; }));
    }
    if ((C !== null))
    {
      GaussianElimination.Set((numRows * numCols), C, GteRef(() => Y, v => { Y = v; }));
    }
    let matInvM = LexicoArray2.$create0(numRows, numRows, inverseM);
    let matY = LexicoArray2.$create0(numRows, numCols, Y);
    let colIndex = GteArray(numRows, () => 0);
    let rowIndex = GteArray(numRows, () => 0);
    let pivoted = GteArray(numRows, () => false);
    let zero = 0;
    let one = 1;
    let odd = false;
    determinant.value = one;
    let i1 = 0, i2 = 0, row = 0, col = 0;
    for (let i0 = 0; (i0 < numRows); (i0++))
    {
      let maxValue = zero;
      for (i1 = 0; (i1 < numRows); (i1++))
      {
        if ((!GetElement(pivoted, i1)))
        {
          for (i2 = 0; (i2 < numRows); (i2++))
          {
            if ((!GetElement(pivoted, i2)))
            {
              let value = matInvM.get_Item(i1, i2);
              let absValue = (((value >= zero) ? value : (-value)));
              if ((absValue > maxValue))
              {
                maxValue = absValue;
                row = i1;
                col = i2;
              }
            }
          }
        }
      }
      if ((DotNetMath.Abs((maxValue - zero)) < 5E-324))
      {
        if (wantInverse)
        {
          GaussianElimination.Set(numElements, null, GteRef(() => inverseM, v => { inverseM = v; }));
        }
        determinant.value = zero;
        if ((B !== null))
        {
          GaussianElimination.Set(numRows, null, GteRef(() => X, v => { X = v; }));
        }
        if ((C !== null))
        {
          GaussianElimination.Set((numRows * numCols), null, GteRef(() => Y, v => { Y = v; }));
        }
        return false;
      }
      SetElement(pivoted, col, true);
      if ((row !== col))
      {
        odd = (!odd);
        for (let i = 0; (i < numRows); (i++))
        {
          let tmp = matInvM.get_Item(row, i);
          matInvM.set_Item(row, i, matInvM.get_Item(col, i));
          matInvM.set_Item(col, i, tmp);
        }
        if ((B !== null))
        {
          let tmp = GetElement(X, row);
          SetElement(X, row, GetElement(X, col));
          SetElement(X, col, tmp);
        }
        if ((C !== null))
        {
          for (let i = 0; (i < numCols); (i++))
          {
            let tmp = matY.get_Item(row, i);
            matY.set_Item(row, i, matY.get_Item(col, i));
            matY.set_Item(col, i, tmp);
          }
        }
      }
      SetElement(rowIndex, i0, row);
      SetElement(colIndex, i0, col);
      let diagonal = matInvM.get_Item(col, col);
      determinant.value *= diagonal;
      let inv = (one / diagonal);
      matInvM.set_Item(col, col, one);
      for (i2 = 0; (i2 < numRows); (i2++))
      {
        matInvM.set_Item(col, i2, (matInvM.get_Item(col, i2) * inv));
      }
      if ((B !== null))
      {
        SetElement(X, col, (GetElement(X, col) * inv));
      }
      if ((C !== null))
      {
        for (i2 = 0; (i2 < numCols); (i2++))
        {
          matY.set_Item(col, i2, (matY.get_Item(col, i2) * inv));
        }
      }
      for (i1 = 0; (i1 < numRows); (i1++))
      {
        if ((i1 !== col))
        {
          let save = matInvM.get_Item(i1, col);
          matInvM.set_Item(i1, col, zero);
          for (i2 = 0; (i2 < numRows); (i2++))
          {
            matInvM.set_Item(i1, i2, (matInvM.get_Item(i1, i2) - MultiplyDouble(matInvM.get_Item(col, i2), save)));
          }
          if ((B !== null))
          {
            SetElement(X, i1, (GetElement(X, i1) - MultiplyDouble(GetElement(X, col), save)));
          }
          if ((C !== null))
          {
            for (i2 = 0; (i2 < numCols); (i2++))
            {
              matY.set_Item(i1, i2, (matY.get_Item(i1, i2) - MultiplyDouble(matY.get_Item(col, i2), save)));
            }
          }
        }
      }
    }
    if (wantInverse)
    {
      for (i1 = (numRows - 1); (i1 >= 0); (i1--))
      {
        if ((GetElement(rowIndex, i1) !== GetElement(colIndex, i1)))
        {
          for (i2 = 0; (i2 < numRows); (i2++))
          {
            let tmp = matInvM.get_Item(i2, GetElement(rowIndex, i1));
            matInvM.set_Item(i2, GetElement(rowIndex, i1), matInvM.get_Item(i2, GetElement(colIndex, i1)));
            matInvM.set_Item(i2, GetElement(colIndex, i1), tmp);
          }
        }
      }
    }
    if (odd)
    {
      determinant.value = (-determinant.value);
    }
    return true;
  }
  static Set(numElements, source, target) {
    target.value = GteArray(numElements, () => 0);
    if ((source !== null))
    {
      GteCopyTo(source, target.value, 0);
    }
  }
}
