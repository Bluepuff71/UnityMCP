namespace UnityMCP.Editor.Utilities
{
    /// <summary>
    /// Provides commonly used Unity-related constants.
    /// </summary>
    public static class UnityConstants
    {
        /// <summary>
        /// The total number of layers available in Unity (0-31).
        /// </summary>
        public const int TotalLayerCount = 32;

        /// <summary>
        /// The maximum value for a color byte component (used for converting 0-255 range to 0-1 range).
        /// </summary>
        public const float ColorByteMax = 255f;

        /// <summary>
        /// Windows error code for HTTP operation aborted (ERROR_OPERATION_ABORTED).
        /// This is used to detect when an HTTP listener is intentionally stopped.
        /// </summary>
        public const int HttpOperationAborted = 995;
    }
}
