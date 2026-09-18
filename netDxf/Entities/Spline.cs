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

using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>
    /// Represents a spline curve <see cref="EntityObject">entity</see> (NURBS Non-Uniform Rational B-Splines).
    /// </summary>
    public partial class Spline :
        EntityObject
    {
        #region private fields

        public const short MaxDegree = 10;

        private readonly Vector3[] fitPoints;
        private readonly SplineCreationMethod creationMethod;
        private Vector3? startTangent;
        private Vector3? endTangent;
        private readonly Vector3[] controlPoints;
        private readonly double[] weights;
        private double[] knots;
        private readonly short degree;
        private bool isClosedPeriodic;

        private SplineKnotParameterization knotParameterization = SplineKnotParameterization.FitChord;
        private double knotTolerance = 0.0000001;
        private double ctrlPointTolerance = 0.0000001;
        private double fitTolerance = 0.0000000001;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="fitPoints">Spline fit points.</param>
        /// <remarks>
        /// The resulting spline curve will be created from a list of cubic bezier curves that passes through the specified fit points.
        /// </remarks>
        public Spline(IEnumerable<Vector3> fitPoints)
            : this(new FitPointSnapshot(fitPoints))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="curves">List of cubic bezier curves.</param>
        public Spline(IEnumerable<BezierCurveQuadratic> curves)
            : this(curves, 2)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="curves">List of cubic bezier curves.</param>
        public Spline(IEnumerable<BezierCurveCubic> curves)
            : this(curves, 3)
        {
        }

        private Spline(IEnumerable<BezierCurve> curves, short degree)
            : base(EntityType.Spline, DxfObjectCode.Spline)
        {
            // control points and fit points
            if (curves == null) throw new ArgumentNullException(nameof(curves));
            List<Vector3> ctrList = new List<Vector3>();
            List<double> wList = new List<double>();

            foreach (BezierCurve curve in curves)
            {
                if (curve == null) throw new ArgumentException("Curve sequences cannot contain null elements.", nameof(curves));
                foreach (Vector3 point in curve.ControlPoints)
                {
                    ctrList.Add(point);
                    wList.Add(1.0);
                }
            }

            if (ctrList.Count == 0) throw new ArgumentException("At least one Bezier curve is required.", nameof(curves));
            this.controlPoints = ctrList.ToArray();
            this.weights = wList.ToArray();
            this.degree = degree;
            this.isClosedPeriodic = false;
            this.fitPoints = new Vector3[0];
            this.creationMethod = SplineCreationMethod.ControlPoints;
            this.knots = ÇreateBezierKnotVector(this.controlPoints.Length, this.degree);
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="controlPoints">Spline control points.</param>
        /// <param name="weights">Spline control weights. Pass null to set the default weights as 1.0.</param>
        /// <remarks>By default the degree of the spline is equal three.</remarks>
        public Spline(IEnumerable<Vector3> controlPoints, IEnumerable<double> weights)
            : this(controlPoints, weights, 3, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="controlPoints">Spline control points.</param>
        /// <param name="weights">Spline control weights. Pass null to set the default weights as 1.0.</param>
        /// <param name="degree">Degree of the spline curve.  Valid values are 1 (linear), degree 2 (quadratic), degree 3 (cubic), and so on up to degree 10.</param>
        public Spline(IEnumerable<Vector3> controlPoints, IEnumerable<double> weights, short degree)
            : this(controlPoints, weights, degree, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="controlPoints">Spline control points.</param>
        /// <param name="weights">Spline control weights.  If null the weights vector will be automatically initialized with 1.0.</param>
        /// <param name="closedPeriodic">Sets if the spline as periodic closed (default false).</param>
        /// <remarks>By default the degree of the spline is equal three.</remarks>
        public Spline(IEnumerable<Vector3> controlPoints, IEnumerable<double> weights, bool closedPeriodic)
            : this(controlPoints, weights, 3, closedPeriodic)
        {
        }


        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="controlPoints">Spline control points.</param>
        /// <param name="weights">Spline control weights.  If null the weights vector will be automatically initialized with 1.0.</param>
        /// <param name="degree">Degree of the spline curve.  Valid values are 1 (linear), degree 2 (quadratic), degree 3 (cubic), and so on up to degree 10.</param>
        /// <param name="closedPeriodic">Sets if the spline as periodic closed (default false).</param>
        public Spline(IEnumerable<Vector3> controlPoints, IEnumerable<double> weights, short degree, bool closedPeriodic)
            : base(EntityType.Spline, DxfObjectCode.Spline)
        {
            // spline degree
            if (degree < 1 || degree > MaxDegree)
            {
                throw new ArgumentOutOfRangeException(nameof(degree), degree, "The spline degree valid values range from 1 to 10.");
            }
            this.degree = degree;

            // control points
            if (controlPoints == null)
            {
                throw new ArgumentNullException(nameof(controlPoints));
            }

            // create control points
            this.controlPoints = controlPoints.ToArray();
            if (this.controlPoints.Length < 2)
            {
                throw new ArgumentException("The number of control points must be equal or greater than 2.");
            }

            if (this.controlPoints.Length < degree + 1)
            {
                throw new ArgumentException("The number of control points must be equal or greater than the spline degree + 1.");
            }

            // create weights
            if (weights == null)
            {
                this.weights = new double[this.controlPoints.Length];
                for (int i = 0; i < this.controlPoints.Length; i++)
                {
                    this.weights[i] = 1.0;
                }
            }
            else
            {
                this.weights = weights.ToArray();
                if (this.weights.Length != this.controlPoints.Length)
                {
                    throw new ArgumentException("The number of control points must be the same as the number of weights.", nameof(weights));
                }
            }

            this.isClosedPeriodic = closedPeriodic;
            this.creationMethod = SplineCreationMethod.ControlPoints;
            this.fitPoints = new Vector3[0];
            this.knots = CreateKnotVector(this.controlPoints.Length, this.degree, this.isClosedPeriodic);
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="controlPoints">Spline control points.</param>
        /// <param name="weigths">Spline control weights.  If null the weights vector will be automatically initialized with 1.0.</param>
        /// <param name="knots">Spline knot vector.</param>
        /// <param name="degree">Degree of the spline curve. Valid values are 1 (linear), degree 2 (quadratic), degree 3 (cubic), and so on up to degree 10.</param>
        /// <param name="closedPeriodic">Sets if the spline as periodic closed (default false).</param>
        public Spline(IEnumerable<Vector3> controlPoints, IEnumerable<double> weigths, IEnumerable<double> knots, short degree, bool closedPeriodic)
            : this(controlPoints, weigths, knots, degree, null, SplineCreationMethod.ControlPoints, closedPeriodic)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Spline</c> class.
        /// </summary>
        /// <param name="controlPoints">Spline control points.</param>
        /// <param name="weights">Spline control weights.  If null the weights vector will be automatically initialized with 1.0.</param>
        /// <param name="knots">Spline knot vector.</param>
        /// <param name="degree">Degree of the spline curve.  Valid values are 1 (linear), degree 2 (quadratic), degree 3 (cubic), and so on up to degree 10.</param>
        /// <param name="fitPoints">Spine fit points.</param>
        /// <param name="method">Spline creation method.</param>
        /// <param name="closedPeriodic">Sets if the spline as periodic closed (default false).</param>
        internal Spline(IEnumerable<Vector3> controlPoints, IEnumerable<double> weights, IEnumerable<double> knots, short degree, IEnumerable<Vector3> fitPoints, SplineCreationMethod method, bool closedPeriodic)
            : base(EntityType.Spline, DxfObjectCode.Spline)
        {
            // spline degree
            if (degree < 1 || degree > MaxDegree)
            {
                throw new ArgumentOutOfRangeException(nameof(degree), degree, "The spline degree valid values range from 1 to 10.");
            }

            this.degree = degree;
            
            // control points
            if (controlPoints == null)
            {
                if (method == SplineCreationMethod.ControlPoints)
                {
                    throw new ArgumentNullException(nameof(controlPoints), "Cannot create a spline without control points if its creation method is with control points.");
                }

                this.controlPoints = new Vector3[0];
            }
            else
            {
                this.controlPoints = controlPoints.ToArray();
                int numControlPoints = this.controlPoints.Length;
                if (numControlPoints < 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(controlPoints), numControlPoints, "The number of control points must be equal or greater than 2.");
                }

                if (numControlPoints < degree + 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(controlPoints), numControlPoints, "The number of control points must be equal or greater than the spline degree + 1.");
                }

                // create weights
                if (weights == null)
                {
                    this.weights = new double[numControlPoints];
                    for (int i = 0; i < numControlPoints; i++)
                    {
                        this.weights[i] = 1.0;
                    }
                }
                else
                {
                    this.weights = weights.ToArray();
                    int numWeights = this.weights.Length;
                    if (numWeights != numControlPoints)
                    {
                        throw new ArgumentException("The number of control points must be the same as the number of weights.", nameof(weights));
                    }
                }

                // knots
                if (knots == null)
                {
                    throw new ArgumentNullException(nameof(knots));
                }

                this.knots = knots.ToArray();
                int numKnots;
                if (closedPeriodic)
                {
                    numKnots = numControlPoints + 2 * degree + 1;
                }
                else
                {
                    numKnots = numControlPoints + degree + 1;
                }
                if (this.knots.Length != numKnots)
                {
                    throw new ArgumentException("Invalid number of knots.");
                }
            }

            // fit points
            if (fitPoints == null)
            {
                if (method == SplineCreationMethod.FitPoints)
                {
                    throw new ArgumentNullException(nameof(fitPoints), "Cannot create a spline without fit points if its creation method is with fit points.");
                }
                this.fitPoints = new Vector3[0];
            }
            else
            {
                this.fitPoints = fitPoints.ToArray();
            }

            this.creationMethod = method;
            this.isClosedPeriodic = closedPeriodic;
        }

        // Copy stored representations directly. A fit-created spline may also
        // carry authored/edited controls, weights and knots; invoking the public
        // fit-point constructor would silently replace that geometry.
        private Spline(Spline source)
            : this(source, EntityType.Spline, DxfObjectCode.Spline)
        {
        }

        // Derived DXF entities retain the stored spline payload, not a refit.
        internal Spline(Spline source, EntityType type, string codeName)
            : base(type, codeName)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            this.controlPoints = (Vector3[]) source.controlPoints.Clone();
            this.weights = source.weights == null ? null : (double[]) source.weights.Clone();
            this.knots = source.knots == null ? null : (double[]) source.knots.Clone();
            this.fitPoints = (Vector3[]) source.fitPoints.Clone();
            this.degree = source.degree;
            this.creationMethod = source.creationMethod;
            this.isClosedPeriodic = source.isClosedPeriodic;
            this.knotParameterization = source.knotParameterization;
            this.knotTolerance = source.knotTolerance;
            this.ctrlPointTolerance = source.ctrlPointTolerance;
            this.fitTolerance = source.fitTolerance;
            this.startTangent = source.startTangent;
            this.endTangent = source.endTangent;
            this.Layer = (Layer) source.Layer.Clone();
            this.Linetype = (Linetype) source.Linetype.Clone();
            this.Color = (AciColor) source.Color.Clone();
            this.Lineweight = source.Lineweight;
            this.Transparency = (Transparency) source.Transparency.Clone();
            this.LinetypeScale = source.LinetypeScale;
            this.Normal = source.Normal;
            this.IsVisible = source.IsVisible;
            source.CopyCommonDataTo(this);
            foreach (XData data in source.XData.Values)
                this.XData.Add((XData) data.Clone());
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets the spline <see cref="Vector3">fit points</see> list.
        /// </summary>
        public IReadOnlyList<Vector3> FitPoints
        {
            get { return this.fitPoints; }
        }

        /// <summary>
        /// Gets or sets the spline curve start tangent.
        /// </summary>
        /// <remarks>Only applicable to splines created with fit points.</remarks>
        public Vector3? StartTangent
        {
            get { return this.startTangent; }
            set { this.startTangent = value; }
        }

        /// <summary>
        /// Gets or sets the spline curve end tangent.
        /// </summary>
        /// <remarks>Only applicable to splines created with fit points.</remarks>
        public Vector3? EndTangent
        {
            get { return this.endTangent; }
            set { this.endTangent = value; }
        }

        /// <summary>
        /// Gets or set the knot parameterization computational method.
        /// </summary>
        /// <remarks>
        /// Not usable. When initializing a Spline through a set of fit points, the resulting spline is approximated creating a list of cubic bezier curves.
        /// It is only informative for splines that has been loaded from a DXF file.
        /// </remarks>
        public SplineKnotParameterization KnotParameterization
        {
            get { return this.knotParameterization; }
            set { this.knotParameterization = value; }
        }

        /// <summary>
        /// Gets the spline creation method.
        /// </summary>
        public SplineCreationMethod CreationMethod
        {
            get { return this.creationMethod; }
        }

        /// <summary>
        /// Gets or sets the knot tolerance.
        /// </summary>
        public double KnotTolerance
        {
            get { return this.knotTolerance; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The knot tolerance must be greater than zero.");
                }

                this.knotTolerance = value;
            }
        }

        /// <summary>
        /// Gets or sets the control point tolerance.
        /// </summary>
        public double CtrlPointTolerance
        {
            get { return this.ctrlPointTolerance; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The control point tolerance must be greater than zero.");
                }

                this.ctrlPointTolerance = value;
            }
        }

        /// <summary>
        /// Gets or sets the fit point tolerance.
        /// </summary>
        public double FitTolerance
        {
            get { return this.fitTolerance; }
            set
            {
                if (value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The fit tolerance must be greater than zero.");
                }

                this.fitTolerance = value;
            }
        }

        /// <summary>
        /// Gets or sets the polynomial degree of the resulting spline.
        /// </summary>
        /// <remarks>
        /// Valid values are 1 (linear), degree 2 (quadratic), degree 3 (cubic), and so on up to degree 10.
        /// </remarks>
        public short Degree
        {
            get { return this.degree; }
        }

        /// <summary>
        /// Gets if the spline is closed.
        /// </summary>
        /// <remarks>
        /// An Spline is closed when the start and end control points are the same.
        /// </remarks>
        public bool IsClosed
        {
            get
            {
                return this.controlPoints[0].Equals(this.controlPoints[this.controlPoints.Length - 1]);
            }
        }

        /// <summary>
        /// Gets or sets if the spline is closed and periodic.
        /// </summary>
        /// <remarks>
        /// A periodic spline is always closed creating a smooth continuity at the end points. <br />
        /// Changing the property will rebuild the knot vector.
        /// </remarks>
        public bool IsClosedPeriodic
        {
            get { return this.isClosedPeriodic; }
            set
            {
                this.knots = CreateKnotVector(this.controlPoints.Length, this.degree, value);
                this.isClosedPeriodic = value;
            }
        }

        /// <summary>
        /// Gets the spline <see cref="Vector3">control points</see> list.
        /// </summary>
        public Vector3[] ControlPoints
        {
            get { return this.controlPoints; }
        }

        /// <summary>
        /// Gets the spline control points weights list.
        /// </summary>
        public double[] Weights
        {
            get { return this.weights; }
        }

        /// <summary>
        /// Gets the spline knot vector.
        /// </summary>
        /// <remarks>By default a uniform knot vector is created.</remarks>
        public double[] Knots
        {
            get { return this.knots; }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Switch the spline direction.
        /// </summary>
        public void Reverse()
        {
            // Reverse the parameterization, not just the control polygon:
            // U'[i] = a + b - U[m-i], where [a,b] is the active knot domain.
            // Prepare all values before changing any caller-visible arrays.
            double[] reversedKnots = null;
            if (this.knots != null)
            {
                double a = this.knots[this.degree];
                double b = this.knots[this.knots.Length - this.degree - 1];
                reversedKnots = new double[this.knots.Length];
                for (int i = 0; i < this.knots.Length; i++)
                {
                    double knot = this.knots[i];
                    if (double.IsNaN(knot) || double.IsInfinity(knot) ||
                        (i != 0 && knot < this.knots[i - 1]))
                        throw new InvalidOperationException("Spline reversal requires finite nondecreasing knots.");
                    // Preserve the active endpoints exactly and avoid an
                    // unnecessary overflow of a+b on large same-sign domains.
                    double reflected = knot == a ? b : knot == b ? a :
                        (a >= 0.0) != (b >= 0.0) ? (a + b) - knot :
                        ((a >= 0.0) == (knot >= 0.0) ? (a - knot) + b :
                         (b >= 0.0) == (knot >= 0.0) ? (b - knot) + a : (a + b) - knot);
                    if (double.IsNaN(reflected) || double.IsInfinity(reflected))
                        throw new InvalidOperationException("Reversed spline knots exceed the finite double range.");
                    reversedKnots[this.knots.Length - 1 - i] = reflected;
                }
            }

            Array.Reverse(this.fitPoints);
            Array.Reverse(this.controlPoints);
            if (this.weights != null) Array.Reverse(this.weights);
            if (this.isClosedPeriodic)
            {
                // Serialized/evaluated periodic controls prepend the last p
                // stored controls. Rotate the reversed stored polygon by p so
                // that its expanded polygon is the exact reverse of the old one.
                int p = this.degree;
                Array.Reverse(this.controlPoints, 0, p);
                Array.Reverse(this.controlPoints, p, this.controlPoints.Length - p);
                Array.Reverse(this.controlPoints);
                if (this.weights != null)
                {
                    Array.Reverse(this.weights, 0, p);
                    Array.Reverse(this.weights, p, this.weights.Length - p);
                    Array.Reverse(this.weights);
                }
            }
            if (reversedKnots != null) Array.Copy(reversedKnots, this.knots, reversedKnots.Length);
            Vector3? tmp = this.startTangent;
            this.startTangent = -this.endTangent;
            this.endTangent = -tmp;
        }

        /// <summary>
        /// Sets all control point weights to the specified number.
        /// </summary>
        /// <param name="weight">Control point weight.</param>
        public void SetUniformWeights(double weight)
        {
            for (int i = 0; i < this.weights.Length; i++)
            {
                this.weights[i] = weight;
            }
        }

        /// <summary>
        /// Converts the spline in a list of vertexes.
        /// </summary>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A list vertexes that represents the spline.</returns>
        /// <remarks>
        /// Periodic evaluation uses the stored active knot domain and requires
        /// positive finite weights and cyclic strictly increasing knot spans.
        /// Scaled arithmetic leaves stored arrays unchanged. Ill-conditioned local
        /// samples use bounded exact-rational evaluation; unsupported or unrepresentable
        /// samples throw instead of returning an artificial origin point.
        /// </remarks>
        public List<Vector3> PolygonalVertexes(int precision)
        {
            return NurbsEvaluator(this.controlPoints.ToArray(), this.weights.ToArray(), this.knots, this.degree, this.IsClosed, this.isClosedPeriodic, precision);
        }

        /// <summary>
        /// Converts the spline in a Polyline3D.
        /// </summary>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A new instance of <see cref="Polyline3D">Polyline3D</see> that represents the spline.</returns>
        public Polyline3D ToPolyline3D(int precision)
        {
            return this.ConvertToPolyline3D(precision);
        }

        /// <summary>
        /// Converts the spline in a Polyline2D.
        /// </summary>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A new instance of <see cref="Polyline2D">Polyline2D</see> that represents the spline.</returns>
        /// <remarks>
        /// The result is projected onto the zero-elevation object XY plane defined by the normal.
        /// Use ToPolyline2D(int, double) to select an explicit parallel plane.
        /// Converted output has independently copied ordinary appearance and XData; unsupported dependencies reject.
        /// </remarks>
        public Polyline2D ToPolyline2D(int precision)
        {
            return this.ToPolyline2D(precision, 0.0);
        }

        /// <summary>
        /// Calculate points along a NURBS curve.
        /// </summary>
        /// <param name="controls">List of spline control points.</param>
        /// <param name="weights">Spline control weights. If null the weights vector will be automatically initialized with 1.0.</param>
        /// <param name="knots">List of spline knot points. If null the knot vector will be automatically generated.</param>
        /// <param name="degree">Spline degree.</param>
        /// <param name="isClosed">Specifies if the spline is closed.</param>
        /// <param name="isClosedPeriodic">Specifies if the spline is closed and periodic.</param>
        /// <param name="precision">Number of vertexes generated.</param>
        /// <returns>A list vertexes that represents the spline.</returns>
        /// <remarks>
        /// NURBS evaluator provided by mikau16 based on Michael V. implementation, roughly follows the notation of http://cs.mtu.edu/~shene/PUBLICATIONS/2004/NURBS.pdf
        /// Added a few modifications to make it work for open, closed, and periodic closed splines.
        /// Nonperiodic curves use the active domain knots[degree] through knots[controls.Length].
        /// Open samples include the evaluated right endpoint; closed samples exclude a duplicated endpoint.
        /// Nonperiodic evaluation requires finite source data, valid knot multiplicities, and 2 to 1000000 samples.
        /// Sampled rational poles or unrepresentable sampling parameters throw before a result is returned.
        /// </remarks>
        public static List<Vector3> NurbsEvaluator(Vector3[] controls, double[] weights, double[] knots, int degree, bool isClosed, bool isClosedPeriodic, int precision)
        {
            if (!isClosedPeriodic) return EvaluateNonPeriodicSpline(controls, weights, knots, degree, isClosed, precision);

            if (precision < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(precision), precision, "The precision must be equal or greater than two.");
            }

            // control points
            if (controls == null)
            {
                throw new ArgumentNullException(nameof(controls), "A spline entity with control points is required.");
            }

            int numCtrlPoints = controls.Length;

            if (numCtrlPoints == 0)
            {
                throw new ArgumentException("A spline entity with control points is required.", nameof(controls));
            }

            // weights
            if (weights == null)
            {
                // give the default 1.0 to the control points weights
                weights = new double[numCtrlPoints];
                for (int i = 0; i < numCtrlPoints; i++)
                {
                    weights[i] = 1.0;
                }
            }
            else if (weights.Length != numCtrlPoints)
            {
                throw new ArgumentException("The number of control points must be the same as the number of weights.", nameof(weights));
            }

            // knots
            if (knots == null)
            {
                knots = CreateKnotVector(numCtrlPoints, degree, isClosedPeriodic);
            }
            else
            {
                int numKnots;
                if (isClosedPeriodic)
                {
                    numKnots = numCtrlPoints + 2 * degree + 1;
                }
                else
                {
                    numKnots = numCtrlPoints + degree + 1;
                }
                if (knots.Length != numKnots)
                {
                    throw new ArgumentException("Invalid number of knots.");
                }
            }

            if (isClosedPeriodic) PeriodicSplineData.Validate(controls, weights, knots, degree);
            Vector3[] ctrl;
            double[] w;
            if (isClosedPeriodic)
            {
                ctrl = new Vector3[numCtrlPoints + degree];
                w = new double[numCtrlPoints + degree];
                for (int i = 0; i < degree; i++)
                {
                    int index = numCtrlPoints - degree + i;
                    ctrl[i] = controls[index];
                    w[i] = weights[index];
                }

                controls.CopyTo(ctrl, degree);
                weights.CopyTo(w, degree);
            }
            else
            {
                ctrl = controls;
                w = weights;
            }

            double uStart;
            double uEnd;
            List<Vector3> vertexes = new List<Vector3>();

            if (isClosedPeriodic)
            {
                uStart = knots[degree];
                uEnd = knots[knots.Length - degree - 1];
            }
            else if (isClosed)
            {
                uStart = knots[0];
                uEnd = knots[knots.Length - 1];
            }
            else
            {
                precision -= 1;
                uStart = knots[0];
                uEnd = knots[knots.Length - 1];
            }

            double uDelta = (uEnd - uStart) / precision;
            if (isClosedPeriodic && (uDelta <= 0 || uStart + uDelta <= uStart || uEnd - uDelta >= uEnd))
                throw new ArgumentException("The requested periodic SPLINE sampling parameters cannot be represented.");
            double previous = uStart;
            for (int i = 0; i < precision; i++)
            {
                double u = uStart + uDelta * i;
                if (isClosedPeriodic && (i > 0 && u <= previous || u >= uEnd))
                    throw new ArgumentException("The requested periodic SPLINE sampling parameters cannot be represented.");
                previous = u;
                vertexes.Add(C(ctrl, w, knots, degree, u, isClosedPeriodic));
            }

            if (!(isClosed || isClosedPeriodic))
            {
                vertexes.Add(ctrl[ctrl.Length - 1]);
            }

            return vertexes;
        }

        #endregion

        #region private methods

        private static double[] CreateKnotVector(int numControlPoints, int degree, bool isPeriodic)
        {
            // create knot vector
            int numKnots;
            double[] knots;

            if (!isPeriodic)
            {
                numKnots = numControlPoints + degree + 1;
                knots = new double[numKnots];

                int i;
                for (i = 0; i <= degree; i++)
                {
                    knots[i] = 0.0;
                }

                for (; i < numControlPoints; i++)
                {
                    knots[i] = i - degree;
                }

                for (; i < numKnots; i++)
                {
                    knots[i] = numControlPoints - degree;
                }
            }
            else
            {
                numKnots = numControlPoints + 2 * degree + 1;
                knots = new double[numKnots];

                double factor = 1.0 / (numControlPoints - degree);
                for (int i = 0; i < numKnots; i++)
                {
                    knots[i] = (i - degree) * factor;
                }
            }

            return knots;
        }

        private static double[] ÇreateBezierKnotVector(int numControlPoints, int degree)
        {
            // create knot vector
            int numKnots = numControlPoints + degree + 1;
            double[] knots = new double[numKnots];

            int np = degree + 1;
            int nc =  numKnots / np;
            // There is one knot group at each span boundary: N spans have
            // N + 1 groups. Dividing by the group count stretches the last span.
            double fact = 1.0 / (nc - 1);
            int index = 1;

            for (int i = 0; i < numKnots;)
            {
                double knot;

                if (i < np)
                {
                    knot = 0.0;
                }
                else if (i >= numKnots - np)
                {
                    knot = 1.0;
                }
                else
                {
                    knot = fact * index;
                    index += 1;
                }

                for (int j = 0; j < np; j++)
                {
                    knots[i] = knot;
                    i += 1;
                }
            }

            return knots;
        }

        private static Vector3 C(Vector3[] ctrlPoints, double[] weights, double[] knots, int degree, double u, bool strictPeriodic)
        {
            if (strictPeriodic) return PeriodicPoint(ctrlPoints, weights, knots, degree, u);
            Vector3 vectorSum = Vector3.Zero;
            double denominatorSum = 0.0;

            // optimization suggested by ThVoss
            for (int i = 0; i < ctrlPoints.Length; i++)
            {
                double n = N(knots, i, degree, u);
                denominatorSum += n * weights[i];
                vectorSum += weights[i] * n * ctrlPoints[i];
            }

            // avoid possible divided by zero error, this should never happen
            if (Math.Abs(denominatorSum) < double.Epsilon)
            {
                return Vector3.Zero;
            }

            return (1.0 / denominatorSum) * vectorSum;
        }

        private static Vector3 PeriodicPoint(Vector3[] controls, double[] weights, double[] knots, int degree, double parameter)
        {
            // Only degree + 1 controls have support at a sample. Keep products
            // as mantissa/exponent pairs until the final quotient: even a
            // subnormal coefficient can make a finite coordinate contribution.
            int low = degree, high = controls.Length;
            while (low + 1 < high)
            {
                int middle = low + (high - low) / 2;
                if (parameter < knots[middle]) high = middle;
                else low = middle;
            }
            int first = low - degree;
            double[] coefficients = new double[degree + 1];
            int[] exponents = new int[degree + 1];
            for (int i = 0; i <= degree; i++)
            {
                double value = N(knots, first + i, degree, parameter);
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 ||
                    (value > 0 && value < 2.2250738585072014e-308) ||
                    (value == 0 && (i < degree || parameter > knots[low])))
                    return PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first);
                if (value == 0) continue;
                double basisMantissa = PeriodicMantissa(value, out int basisExponent);
                double weightMantissa = PeriodicMantissa(weights[first + i], out int weightExponent);
                coefficients[i] = basisMantissa * weightMantissa;
                exponents[i] = basisExponent + weightExponent;
            }
            double[] terms = (double[])coefficients.Clone();
            int[] powers = (int[])exponents.Clone();
            double denominator = PeriodicScaledSum(terms, powers, out int denominatorExponent);
            if (denominator <= 0) throw new ArgumentException("The periodic SPLINE evaluation denominator is not representable.");
            Vector3? exactPoint = null;
            return new Vector3(
                PeriodicCoordinate(controls, first, 0, coefficients, exponents, terms, powers, denominator, denominatorExponent, weights, knots, degree, parameter, ref exactPoint),
                PeriodicCoordinate(controls, first, 1, coefficients, exponents, terms, powers, denominator, denominatorExponent, weights, knots, degree, parameter, ref exactPoint),
                PeriodicCoordinate(controls, first, 2, coefficients, exponents, terms, powers, denominator, denominatorExponent, weights, knots, degree, parameter, ref exactPoint));
        }

        private static double PeriodicCoordinate(Vector3[] controls, int first, int axis, double[] coefficients, int[] exponents,
            double[] terms, int[] powers, double denominator, int denominatorExponent, double[] weights, double[] knots,
            int degree, double parameter, ref Vector3? exactPoint)
        {
            if (exactPoint.HasValue) return axis == 0 ? exactPoint.Value.X : axis == 1 ? exactPoint.Value.Y : exactPoint.Value.Z;
            double minimum = double.MaxValue, maximum = -double.MaxValue;
            int largestPower = int.MinValue;
            for (int i = 0; i < coefficients.Length; i++)
            {
                terms[i] = 0; powers[i] = 0;
                if (coefficients[i] == 0) continue;
                Vector3 point = controls[first + i];
                double coordinate = axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;
                minimum = Math.Min(minimum, coordinate); maximum = Math.Max(maximum, coordinate);
                if (coordinate == 0) continue;
                terms[i] = coefficients[i] * PeriodicMantissa(coordinate, out int coordinateExponent);
                powers[i] = exponents[i] + coordinateExponent;
                largestPower = Math.Max(largestPower, powers[i]);
            }
            // Positive rational weights form a convex combination. Preserve
            // constant coordinates exactly, including the binary64 endpoints.
            if (minimum == maximum) return minimum;
            double numerator = PeriodicScaledSum(terms, powers, out int numeratorExponent);
            if (minimum < 0 && maximum > 0)
            {
                int resultPower = int.MinValue;
                if (numerator != 0)
                {
                    PeriodicMantissa(numerator, out int mantissaPower);
                    resultPower = numeratorExponent + mantissaPower;
                }
                // When opposite terms cancel significantly, rounding either
                // the products or the basis can dominate the answer. Evaluate
                // the original binary64 packet exactly for this local sample.
                if (numerator == 0 || largestPower - resultPower >= 4)
                {
                    exactPoint = PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first);
                    return axis == 0 ? exactPoint.Value.X : axis == 1 ? exactPoint.Value.Y : exactPoint.Value.Z;
                }
            }
            double result = PeriodicScale(numerator / denominator, numeratorExponent - denominatorExponent);
            // Rounding at Double.MaxValue must not create an infinity outside
            // the finite convex hull of the controls.
            result = Math.Max(minimum, Math.Min(maximum, result));
            PeriodicSplineData.Finite(result);
            return result;
        }

        private static double PeriodicMantissa(double value, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int field = (int)((bits >> 52) & 0x7ff);
            int adjustment = 0;
            if (field == 0)
            {
                // Multiplication by 2^54 normalizes every nonzero subnormal
                // without rounding or changing its significand.
                value *= 18014398509481984.0;
                bits = BitConverter.DoubleToInt64Bits(value);
                field = (int)((bits >> 52) & 0x7ff);
                adjustment = -54;
            }
            exponent = field - 1023 + adjustment;
            return BitConverter.Int64BitsToDouble((bits & unchecked((long)0x800fffffffffffffUL)) | 0x3ff0000000000000L);
        }

        private static double PeriodicScaledSum(double[] terms, int[] powers, out int exponent)
        {
            // Process large terms first so cancellation can expose small terms
            // before their scale is chosen. Compensate each rounded addition.
            Array.Sort(powers, terms);
            double sum = 0, correction = 0;
            exponent = 0;
            for (int i = terms.Length - 1; i >= 0; i--)
            {
                if (terms[i] == 0) continue;
                if (sum == 0 && correction == 0) exponent = powers[i];
                double value = PeriodicScale(terms[i], powers[i] - exponent);
                double next = sum + value;
                correction += Math.Abs(sum) >= Math.Abs(value) ? (sum - next) + value : (value - next) + sum;
                sum = next;
            }
            return sum + correction;
        }

        private static double PeriodicScale(double value, int exponent)
        {
            if (value == 0 || exponent == 0) return value;
            double mantissa = PeriodicMantissa(value, out int valueExponent);
            int combined = valueExponent + exponent;
            long bits = BitConverter.DoubleToInt64Bits(mantissa);
            if (combined > 1023) return value > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (combined < -1075) return BitConverter.Int64BitsToDouble(bits & long.MinValue);
            if (combined >= -1022)
                return BitConverter.Int64BitsToDouble((bits & unchecked((long)0x800fffffffffffffUL)) | ((long)(combined + 1023) << 52));
            // This factor is only 2^-1 through 2^51. The final multiplication
            // is the sole rounding into the subnormal range, including ties.
            return (mantissa * Math.Pow(2.0, combined + 1074)) * double.Epsilon;
        }

        private static double N(double[] knots, int i, int p, double u)
        {
            if (p <= 0)
            {
                if (knots[i] <= u && u < knots[i + 1])
                {
                    return 1;
                }

                return 0.0;
            }

            double leftCoefficient = 0.0;
            if (!(Math.Abs(knots[i + p] - knots[i]) < double.Epsilon))
            {
                leftCoefficient = (u - knots[i]) / (knots[i + p] - knots[i]);
            }

            double rightCoefficient = 0.0; // article contains error here, denominator is Knots[i + p + 1] - Knots[i + 1]
            if (!(Math.Abs(knots[i + p + 1] - knots[i + 1]) < double.Epsilon))
            {
                rightCoefficient = (knots[i + p + 1] - u) / (knots[i + p + 1] - knots[i + 1]);
            }

            return leftCoefficient * N(knots, i, p - 1, u) + rightCoefficient * N(knots, i + 1, p - 1, u);
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
            this.TransformSplineAffine(transformation, translation);
        }

        /// <summary>
        /// Creates a new Spline that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Spline that is a copy of this instance.</returns>
        public override object Clone()
        {
            return new Spline(this);
        }

        #endregion
    }
}
