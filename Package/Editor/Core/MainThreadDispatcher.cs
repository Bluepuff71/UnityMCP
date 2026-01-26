using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Dispatches actions to Unity's main thread using EditorApplication.update.
    /// Works even when Unity is not in focus (unlike delayCall).
    /// </summary>
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();
        private static volatile bool _isRunning;
        private static readonly int _mainThreadId;

        static MainThreadDispatcher()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Start();
        }

        /// <summary>
        /// Starts the dispatcher. Called automatically on editor load.
        /// </summary>
        public static void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            EditorApplication.update += ProcessQueue;
        }

        /// <summary>
        /// Stops the dispatcher.
        /// </summary>
        public static void Stop()
        {
            _isRunning = false;
            EditorApplication.update -= ProcessQueue;
        }

        /// <summary>
        /// Enqueues an action to be executed on the main thread.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _actionQueue.Enqueue(action);
        }

        /// <summary>
        /// Checks if the current thread is the main thread.
        /// </summary>
        /// <returns>True if on the main thread, false otherwise.</returns>
        public static bool IsMainThread()
        {
            return Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        /// <summary>
        /// Dispatches a function to the main thread and waits for completion.
        /// If already on the main thread, executes immediately.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The function to execute on the main thread.</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default 30 seconds).</param>
        /// <returns>The result of the function.</returns>
        /// <exception cref="TimeoutException">Thrown if the operation times out.</exception>
        public static T DispatchAndWait<T>(Func<T> func, int timeoutMs = 30000)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            if (IsMainThread())
                return func();

            var completionSource = new TaskCompletionSource<T>();

            Enqueue(() =>
            {
                try
                {
                    completionSource.SetResult(func());
                }
                catch (Exception ex)
                {
                    completionSource.SetException(ex);
                }
            });

            if (!completionSource.Task.Wait(timeoutMs))
                throw new TimeoutException("Main thread dispatch timed out");

            return completionSource.Task.Result;
        }

        /// <summary>
        /// Dispatches an action to the main thread and waits for completion.
        /// If already on the main thread, executes immediately.
        /// </summary>
        /// <param name="action">The action to execute on the main thread.</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default 30 seconds).</param>
        /// <exception cref="TimeoutException">Thrown if the operation times out.</exception>
        public static void DispatchAndWait(Action action, int timeoutMs = 30000)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (IsMainThread())
            {
                action();
                return;
            }

            var completionSource = new TaskCompletionSource<bool>();

            Enqueue(() =>
            {
                try
                {
                    action();
                    completionSource.SetResult(true);
                }
                catch (Exception ex)
                {
                    completionSource.SetException(ex);
                }
            });

            if (!completionSource.Task.Wait(timeoutMs))
                throw new TimeoutException("Main thread dispatch timed out");
        }

        private static void ProcessQueue()
        {
            // Process all pending actions
            while (_actionQueue.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[MainThreadDispatcher] Error executing action: {ex}");
                }
            }
        }
    }
}
