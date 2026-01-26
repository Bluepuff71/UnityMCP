using Newtonsoft.Json;

namespace UnityMCP.Editor.Utilities
{
    /// <summary>
    /// Provides cached JSON serialization settings for consistent serialization across the application.
    /// </summary>
    public static class JsonUtilities
    {
        /// <summary>
        /// Default JSON serializer settings configured for Unity object serialization.
        /// </summary>
        /// <remarks>
        /// These settings are configured with:
        /// <list type="bullet">
        /// <item><see cref="ReferenceLoopHandling.Ignore"/> - Prevents infinite loops when serializing circular references</item>
        /// <item><see cref="NullValueHandling.Include"/> - Includes null values in the output for explicit representation</item>
        /// <item><see cref="Formatting.Indented"/> - Produces human-readable formatted output</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// string json = JsonConvert.SerializeObject(myObject, JsonUtilities.DefaultSettings);
        /// </code>
        /// </example>
        public static readonly JsonSerializerSettings DefaultSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.Indented
        };
    }
}
