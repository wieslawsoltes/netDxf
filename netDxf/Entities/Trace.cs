#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>
    /// Represents a trace <see cref="EntityObject">entity</see>.
    /// </summary>
    /// <remarks>
    /// The trace entity has exactly the same graphical representation as the Solid, and its functionality is exactly the same.
    /// It is recommended to use the more common Solid entity instead.
    /// </remarks>
    public class Trace :
        EntityObject
    {
        #region private fields

        private Vector2 firstVertex;
        private Vector2 secondVertex;
        private Vector2 thirdVertex;
        private Vector2 fourthVertex;
        private double elevation;
        private double thickness;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Trace</c> class.
        /// </summary>
        public Trace()
            : this(Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Trace</c> class.
        /// </summary>
        /// <param name="firstVertex">Trace <see cref="Vector2">first vertex</see> in OCS (object coordinate system).</param>
        /// <param name="secondVertex">Trace <see cref="Vector2">second vertex</see> in OCS (object coordinate system).</param>
        /// <param name="thirdVertex">Trace <see cref="Vector2">third vertex</see> in OCS (object coordinate system).</param>
        public Trace(Vector2 firstVertex, Vector2 secondVertex, Vector2 thirdVertex)
            : this(new Vector2(firstVertex.X, firstVertex.Y),
                new Vector2(secondVertex.X, secondVertex.Y),
                new Vector2(thirdVertex.X, thirdVertex.Y),
                new Vector2(thirdVertex.X, thirdVertex.Y))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Trace</c> class.
        /// </summary>
        /// <param name="firstVertex">Trace <see cref="Vector2">first vertex</see> in OCS (object coordinate system).</param>
        /// <param name="secondVertex">Trace <see cref="Vector2">second vertex</see> in OCS (object coordinate system).</param>
        /// <param name="thirdVertex">Trace <see cref="Vector2">third vertex</see> in OCS (object coordinate system).</param>
        /// <param name="fourthVertex">Trace <see cref="Vector2">fourth vertex</see> in OCS (object coordinate system).</param>
        public Trace(Vector2 firstVertex, Vector2 secondVertex, Vector2 thirdVertex, Vector2 fourthVertex)
            : base(EntityType.Trace, DxfObjectCode.Trace)
        {
            this.firstVertex = firstVertex;
            this.secondVertex = secondVertex;
            this.thirdVertex = thirdVertex;
            this.fourthVertex = fourthVertex;
            this.elevation = 0.0;
            this.thickness = 0.0;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the first trace <see cref="Vector3">vertex in OCS (object coordinate system).</see>.
        /// </summary>
        public Vector2 FirstVertex
        {
            get { return this.firstVertex; }
            set { this.firstVertex = value; }
        }

        /// <summary>
        /// Gets or sets the second trace <see cref="Vector3">vertex in OCS (object coordinate system).</see>.
        /// </summary>
        public Vector2 SecondVertex
        {
            get { return this.secondVertex; }
            set { this.secondVertex = value; }
        }

        /// <summary>
        /// Gets or sets the third trace <see cref="Vector3">vertex in OCS (object coordinate system).</see>.
        /// </summary>
        public Vector2 ThirdVertex
        {
            get { return this.thirdVertex; }
            set { this.thirdVertex = value; }
        }

        /// <summary>
        /// Gets or sets the fourth trace <see cref="Vector3">vertex in OCS (object coordinate system).</see>.
        /// </summary>
        public Vector2 FourthVertex
        {
            get { return this.fourthVertex; }
            set { this.fourthVertex = value; }
        }

        /// <summary>
        /// Gets or sets the trace elevation.
        /// </summary>
        /// <remarks>This is the distance from the origin to the plane of the trace.</remarks>
        public double Elevation
        {
            get { return this.elevation; }
            set { this.elevation = value; }
        }

        /// <summary>
        /// Gets or sets the thickness of the trace.
        /// </summary>
        public double Thickness
        {
            get { return this.thickness; }
            set { this.thickness = value; }
        }

        #endregion

        #region overrides

        /// <summary>
        /// Moves, scales, and/or rotates the current entity given a 3x3 transformation matrix and a translation vector.
        /// </summary>
        /// <param name="transformation">Transformation matrix.</param>
        /// <param name="translation">Translation vector.</param>
        /// <remarks>Matrix3 adopts the convention of using column vectors to represent a transformation matrix.</remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            PlanarEntityTransform candidate = PlanarEntityTransform.Prepare(transformation, translation,
                new[] { this.firstVertex, this.secondVertex, this.thirdVertex, this.fourthVertex },
                this.Normal, this.elevation, this.thickness);
            if (!candidate.Changed) return;
            this.Normal = candidate.Normal;
            this.firstVertex = candidate.Vertexes[0];
            this.secondVertex = candidate.Vertexes[1];
            this.thirdVertex = candidate.Vertexes[2];
            this.fourthVertex = candidate.Vertexes[3];
            this.elevation = candidate.Elevation;
            this.thickness = candidate.Thickness;
            this.ClearProxyGraphics();
        }

        /// <summary>
        /// Applies a finite affine matrix; projective and unrepresentable results reject before mutation.
        /// </summary>
        /// <param name="transformation">Affine transformation matrix using column vectors.</param>
        public override void TransformBy(Matrix4 transformation)
        {
            PlanarEntityTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }

        /// <summary>
        /// Creates a new Trace that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Trace that is a copy of this instance.</returns>
        public override object Clone()
        {
            Trace entity = new Trace
            {
                //EntityObject properties
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                Normal = this.Normal,
                IsVisible = this.IsVisible,
                //Solid properties
                FirstVertex = this.firstVertex,
                SecondVertex = this.secondVertex,
                ThirdVertex = this.thirdVertex,
                FourthVertex = this.fourthVertex,
                Elevation = this.elevation,
                Thickness = this.thickness
            };

            foreach (XData data in this.XData.Values)
            {
                entity.XData.Add((XData) data.Clone());
            }

            this.CopyCommonDataTo(entity);
            return entity;
        }

        #endregion
    }
}