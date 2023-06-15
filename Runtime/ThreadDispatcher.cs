using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Thread Dispatcher allows dispatching actions from the main thread
    /// by invoking them on the Unity context.
    /// </summary>
    public class ThreadDispatcher : MonoBehaviour
    {
        private static ThreadDispatcher _instance;
        private static volatile bool _queued = false;
        private static List<Action> _backlog = new List<Action>(8);
        private static List<Action> _actions = new List<Action>(8);

        public static void RunAsync(Action action)
        {
            ThreadPool.QueueUserWorkItem(o => action());
        }

        public static void RunAsync(Action<object> action, object state)
        {
            ThreadPool.QueueUserWorkItem(o => action(o), state);
        }

        public static void RunOnMainThread(Action action)
        {
            lock (_backlog)
            {
                _backlog.Add(action);
                _queued = true;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_instance == null)
            {
                _instance = new GameObject("Dispatcher").AddComponent<ThreadDispatcher>();
                DontDestroyOnLoad(_instance.gameObject);
            }
        }

        private void Update()
        {
            if (_queued)
            {
                lock (_backlog)
                {
                    var tmp = _actions;
                    _actions = _backlog;
                    _backlog = tmp;
                    _queued = false;
                }

                foreach (var action in _actions)
                    action();

                _actions.Clear();
            }
        }
    }
}