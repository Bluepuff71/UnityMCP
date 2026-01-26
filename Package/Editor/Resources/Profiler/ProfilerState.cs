using UnityEditor;
using UnityEditorInternal;

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
                    profileTarget = connectionIdentifier,
                    connectedProfiler = ProfilerDriver.connectedProfiler
                },
                memory = new
                {
                    usedHeapSize = UnityEngine.Profiling.Profiler.usedHeapSizeLong,
                    usedHeapSizeMB = UnityEngine.Profiling.Profiler.usedHeapSizeLong / (1024.0 * 1024.0),
                    monoHeapSize = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong(),
                    monoHeapSizeMB = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0),
                    monoUsedSize = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
                    monoUsedSizeMB = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0),
                    totalAllocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                    totalAllocatedMemoryMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0),
                    totalReservedMemory = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong(),
                    totalReservedMemoryMB = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0)
                },
                data = new
                {
                    firstFrameIndex = ProfilerDriver.firstFrameIndex,
                    lastFrameIndex = ProfilerDriver.lastFrameIndex
                },
                status = isRecording
                    ? "Profiler is recording"
                    : "Profiler is not recording"
            };
        }
    }
}
