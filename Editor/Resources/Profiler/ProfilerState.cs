using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace UnityMCP.Editor.Resources.Profiler
{
    /// <summary>
    /// Resource provider for profiler state information.
    /// </summary>
    public static class ProfilerState
    {
        /// <summary>
        /// Gets the current profiler recording status and configuration.
        /// </summary>
        /// <returns>Object containing profiler state information.</returns>
        [MCPResource("profiler://state", "Profiler recording status and configuration")]
        public static object GetProfilerState()
        {
            bool isRecording = ProfilerDriver.enabled;
            bool isConnected = ProfilerDriver.IsConnectionEditor();

            // Get profiler connection info
            string connectionIdentifier = ProfilerDriver.GetConnectionIdentifier(
                ProfilerDriver.connectedProfiler);

            // Check memory profiler state
            bool isDeepProfilingEnabled = ProfilerDriver.deepProfiling;

            return new
            {
                recording = new
                {
                    isEnabled = isRecording,
                    isDeepProfiling = isDeepProfilingEnabled
                },
                connection = new
                {
                    isEditor = isConnected,
                    profileTarget = connectionIdentifier,
                    connectedProfiler = ProfilerDriver.connectedProfiler
                },
                memory = new
                {
                    usedHeapSize = Profiler.usedHeapSizeLong,
                    usedHeapSizeMB = Profiler.usedHeapSizeLong / (1024.0 * 1024.0),
                    monoHeapSize = Profiler.GetMonoHeapSizeLong(),
                    monoHeapSizeMB = Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0),
                    monoUsedSize = Profiler.GetMonoUsedSizeLong(),
                    monoUsedSizeMB = Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0),
                    totalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(),
                    totalAllocatedMemoryMB = Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0),
                    totalReservedMemory = Profiler.GetTotalReservedMemoryLong(),
                    totalReservedMemoryMB = Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0)
                },
                data = new
                {
                    firstFrameIndex = ProfilerDriver.firstFrameIndex,
                    lastFrameIndex = ProfilerDriver.lastFrameIndex,
                    maxHistoryLength = ProfilerDriver.maxHistoryLength
                },
                status = isRecording
                    ? "Profiler is recording"
                    : "Profiler is not recording"
            };
        }
    }
}
