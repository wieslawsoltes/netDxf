// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Block } from '../Blocks/Block.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { AciColor } from '../AciColor.js';
import { Lineweight } from '../Lineweight.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Line } from './Line.js';
import { Circle } from './Circle.js';
import { Arc } from './Arc.js';
import { Solid } from './Solid.js';
import { Polyline2D } from './Polyline2D.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
function styled(entity, lineweight) {
  entity.Layer = Layer.Default; entity.Linetype = Linetype.ByBlock; entity.Color = AciColor.ByBlock;
  if (lineweight) entity.Lineweight = Lineweight.ByBlock;
  return entity;
}
/** Fresh original arrowhead block factories. Decimal constants and entity order are retained. */
export class DimensionArrowhead {
  static get Dot() {
    const arrowhead = new Block("_DOT");
    const vertexes = [
    new Polyline2DVertex(-0.25, 0.0, 1.0),
    new Polyline2DVertex(0.25, 0.0, 1.0)
    ];
    const pol = styled(new Polyline2D(vertexes, true), false);
    pol.SetConstantWidth(0.5);
    arrowhead.Entities.Add(pol);
    const line = styled(new Line(new Vector3(-0.5, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line);
    return arrowhead;
  }
  static get DotSmall() {
    const arrowhead = new Block("_DOTSMALL");
    const vertexes = [
    new Polyline2DVertex(-0.0625, 0.0, 1.0),
    new Polyline2DVertex(0.0625, 0.0, 1.0)
    ];
    const pol = styled(new Polyline2D(vertexes, true), false);
    pol.SetConstantWidth(0.5);
    arrowhead.Entities.Add(pol);
    return arrowhead;
  }
  static get DotBlank() {
    const arrowhead = new Block("_DOTBLANK");
    const circle = styled(new Circle(new Vector3(0.0, 0.0, 0.0), 0.5), true);
    arrowhead.Entities.Add(circle);
    const line = styled(new Line(new Vector3(-0.5, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line);
    return arrowhead;
  }
  static get OriginIndicator() {
    const arrowhead = new Block("_ORIGIN");
    const circle = styled(new Circle(new Vector3(0.0, 0.0, 0.0), 0.5), true);
    arrowhead.Entities.Add(circle);
    const line = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line);
    return arrowhead;
  }
  static get OriginIndicator2() {
    const arrowhead = new Block("_ORIGIN2");
    const circle1 = styled(new Circle(new Vector3(0.0, 0.0, 0.0), 0.5), true);
    arrowhead.Entities.Add(circle1);
    const circle2 = styled(new Circle(new Vector3(0.0, 0.0, 0.0), 0.25), true);
    arrowhead.Entities.Add(circle2);
    const line = styled(new Line(new Vector3(-0.5, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line);
    return arrowhead;
  }
  static get Open() {
    const arrowhead = new Block("_OPEN");
    const line1 = styled(new Line(new Vector3(-1.0, 0.1666666666666666, 0.0), new Vector3(0.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, -0.1666666666666666, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line3);
    return arrowhead;
  }
  static get Open90() {
    const arrowhead = new Block("_OPEN90");
    const line1 = styled(new Line(new Vector3(-0.5, 0.5, 0.0), new Vector3(0.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-0.5, -0.5, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line3);
    return arrowhead;
  }
  static get Open30() {
    const arrowhead = new Block("_OPEN30");
    const line1 = styled(new Line(new Vector3(-1.0, 0.26794919, 0.0), new Vector3(0.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, -0.26794919, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line3);
    return arrowhead;
  }
  static get Closed() {
    const arrowhead = new Block("_CLOSED");
    const line1 = styled(new Line(new Vector3(-1.0, 0.1666666666666666, 0.0), new Vector3(0.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, -0.1666666666666666, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(-1.0, 0.1666666666666666, 0.0), new Vector3(-1.0, -0.1666666666666666, 0.0)), true);
    arrowhead.Entities.Add(line3);
    const line4 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line4);
    return arrowhead;
  }
  static get DotSmallBlank() {
    const arrowhead = new Block("_SMALL");
    const circle = styled(new Circle(new Vector3(0.0, 0.0, 0.0), 0.25), true);
    arrowhead.Entities.Add(circle);
    return arrowhead;
  }
  static get None() {
    const arrowhead = new Block("_NONE");
    return arrowhead;
  }
  static get Oblique() {
    const arrowhead = new Block("_OBLIQUE");
    const line = styled(new Line(new Vector3(-0.5, -0.5, 0.0), new Vector3(0.5, 0.5, 0.0)), true);
    arrowhead.Entities.Add(line);
    return arrowhead;
  }
  static get BoxFilled() {
    const arrowhead = new Block("_BOXFILLED");
    const solid = styled(new Solid(new Vector2(-0.5, 0.5), new Vector2(0.5, 0.5), new Vector2(-0.5, -0.5), new Vector2(0.5, -0.5)), false);
    arrowhead.Entities.Add(solid);
    const line = styled(new Line(new Vector3(-0.5, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line);
    return arrowhead;
  }
  static get Box() {
    const arrowhead = new Block("_BOXBLANK");
    const line1 = styled(new Line(new Vector3(-0.5, -0.5, 0.0), new Vector3(0.5, -0.5, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(0.5, -0.5, 0.0), new Vector3(0.5, 0.5, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(0.5, 0.5, 0.0), new Vector3(-0.5, 0.5, 0.0)), true);
    arrowhead.Entities.Add(line3);
    const line4 = styled(new Line(new Vector3(-0.5, 0.5, 0.0), new Vector3(-0.5, -0.5, 0.0)), true);
    arrowhead.Entities.Add(line4);
    const line5 = styled(new Line(new Vector3(-0.5, 0.0, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line5);
    return arrowhead;
  }
  static get ClosedBlank() {
    const arrowhead = new Block("_CLOSEDBLANK");
    const line1 = styled(new Line(new Vector3(-1.0, 0.1666666666666666, 0.0), new Vector3(0.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(0.0, 0.0, 0.0), new Vector3(-1.0, -0.1666666666666666, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(-1.0, 0.1666666666666666, 0.0), new Vector3(-1.0, -0.1666666666666666, 0.0)), true);
    arrowhead.Entities.Add(line3);
    return arrowhead;
  }
  static get DatumTriangleFilled() {
    const arrowhead = new Block("_DATUMFILLED");
    const solid = styled(new Solid(new Vector2(0.0, 0.57735027), new Vector2(-1.0, 0.0), new Vector2(0.0, -0.57735027)), false);
    arrowhead.Entities.Add(solid);
    return arrowhead;
  }
  static get DatumTriangle() {
    const arrowhead = new Block("_DATUMBLANK");
    const line1 = styled(new Line(new Vector3(0.0, 0.5773502700000001, 0.0), new Vector3(-1.0, 0.0, 0.0)), true);
    arrowhead.Entities.Add(line1);
    const line2 = styled(new Line(new Vector3(-1.0, 0.0, 0.0), new Vector3(0.0, -0.5773502700000001, 0.0)), true);
    arrowhead.Entities.Add(line2);
    const line3 = styled(new Line(new Vector3(0.0, -0.5773502700000001, 0.0), new Vector3(0.0, 0.5773502700000001, 0.0)), true);
    arrowhead.Entities.Add(line3);
    return arrowhead;
  }
  static get Integral() {
    const arrowhead = new Block("_INTEGRAL");
    const arc1 = styled(new Arc(new Vector3(0.44488802, -0.09133463, 0.0), 0.4541666700000001, 101.9999999980395, 167.9999999799193), true);
    arrowhead.Entities.Add(arc1);
    const arc2 = styled(new Arc(new Vector3(-0.44488802, 0.09133463, 0.0), 0.4541666700000001, 282.0000000215427, 348.0000000034225), true);
    arrowhead.Entities.Add(arc2);
    return arrowhead;
  }
  static get ArchitecturalTick() {
    const arrowhead = new Block("_ARCHTICK");
    const vertexes = [
    new Polyline2DVertex(-0.5, -0.5),
    new Polyline2DVertex(0.5, 0.5)
    ];
    const pol = styled(new Polyline2D(vertexes, false), false);
    pol.SetConstantWidth(0.15);
    arrowhead.Entities.Add(pol);
    return arrowhead;
  }
}
