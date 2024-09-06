using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ApiClient.Tests
{
    public static class TestUtils
    {
        public static IEnumerator AsCoroutine<T>(this Task<T> task)
        {
            while (!task.IsCompleted) yield return null;
            // if task is faulted, throws the exception
            task.GetAwaiter().GetResult();
        }
    }
}