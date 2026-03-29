namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Ambient context for the current request's session.
    /// Set by MCPProxy before tool invocation, cleared after.
    /// Main-thread only — no threading concerns.
    /// </summary>
    public static class RequestContext
    {
        public static string CurrentSessionId { get; set; }
    }
}
