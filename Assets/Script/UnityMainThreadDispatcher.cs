using System;
using System.Collections.Concurrent;
using UnityEngine;


namespace Footsies
{
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
        private static UnityMainThreadDispatcher instance;

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (instance == null)
                {
                    var obj = new GameObject("UnityMainThreadDispatcher");
                    instance = obj.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(obj);
                }
                return instance;
            }
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void Update()
        {
            while (actions.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        public void Enqueue(Action action)
        {
            actions.Enqueue(action);
        }
    }
}