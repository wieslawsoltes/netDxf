// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Objects
{
    /// <summary>The public FIELD evaluation outcomes accepted by explicit result transactions.</summary>
    /// <remarks>Values match AcDbField evaluation-status codes. This is an outcome, not a flags mask;
    /// NotYetEvaluated is not a completed evaluator result and is intentionally excluded.</remarks>
    public enum DxfFieldResultStatus
    {
        /// <summary>The evaluator completed successfully.</summary>
        Success = 2,
        /// <summary>No appropriate evaluator was available.</summary>
        EvaluatorNotFound = 4,
        /// <summary>The evaluator found a syntax error.</summary>
        SyntaxError = 8,
        /// <summary>The evaluator rejected the field code.</summary>
        InvalidCode = 16,
        /// <summary>The evaluator rejected the evaluation context.</summary>
        InvalidContext = 32,
        /// <summary>The evaluator reported another failure.</summary>
        OtherError = 64
    }

    public sealed partial class DxfFieldResult
    {
        /// <summary>Gets the explicit completed evaluation status; ordinary constructors create success results.</summary>
        public DxfFieldResultStatus Status { get; private set; } = DxfFieldResultStatus.Success;
        /// <summary>Gets the evaluator-defined signed error code, or zero for an ordinary success result.</summary>
        public int ErrorCode { get; private set; }
        /// <summary>Gets decoded evaluator error text, or an empty string for an ordinary success result.</summary>
        public string ErrorMessage { get; private set; } = string.Empty;
        /// <summary>Gets whether this result keeps the exact cache packet of a particular source snapshot.</summary>
        public bool RetainsCachedValue { get { return this.RetainedSnapshot != null; } }
        internal DxfFieldEvaluationSnapshot RetainedSnapshot { get; private set; }

        /// <summary>Creates a failed evaluation bound to a snapshot while retaining its exact cached value and display packets.</summary>
        /// <remarks>The snapshot must belong to the edited FIELD and remain current at publication.
        /// Cache-presence flags are preserved rather than synthesized. Evaluator code and data are not modified.</remarks>
        public static DxfFieldResult Failure(DxfFieldEvaluationSnapshot original, DxfFieldResultStatus status,
            int errorCode, string errorMessage)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            ValidateFailure(status, errorMessage);
            return new DxfFieldResult(original.Value, original.FormattedText, original.ValueDisplayText)
            {
                Status = status, ErrorCode = errorCode, ErrorMessage = errorMessage, RetainedSnapshot = original
            };
        }

        /// <summary>Creates a failed evaluation with an explicit replacement scalar and display fallback.</summary>
        /// <remarks>Replacement values use the same scalar/text limits as successful results.
        /// The result is still failed even when a usable fallback is supplied. No fallback is invented.</remarks>
        public static DxfFieldResult FailureWithValue(DxfFieldResultStatus status, int errorCode, string errorMessage,
            object value, string formattedText, string valueDisplayText = null)
        {
            ValidateFailure(status, errorMessage);
            return new DxfFieldResult(value, formattedText, valueDisplayText)
            { Status = status, ErrorCode = errorCode, ErrorMessage = errorMessage };
        }

        private static void ValidateFailure(DxfFieldResultStatus status, string errorMessage)
        {
            if (status != DxfFieldResultStatus.EvaluatorNotFound && status != DxfFieldResultStatus.SyntaxError &&
                status != DxfFieldResultStatus.InvalidCode && status != DxfFieldResultStatus.InvalidContext &&
                status != DxfFieldResultStatus.OtherError)
                throw new ArgumentOutOfRangeException(nameof(status), "A failure requires one supported completed error status.");
            DxfStoredTableContent.CheckEditableText(errorMessage, nameof(errorMessage));
        }
    }
}
