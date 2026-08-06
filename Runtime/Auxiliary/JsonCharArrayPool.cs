using System;
using Newtonsoft.Json;

namespace ApiClient.Runtime.Auxiliary
{
    /// <summary>
    /// Recycles the char buffers a <see cref="JsonTextReader"/> rents while parsing — its read
    /// buffer plus a string buffer — which are otherwise allocated afresh for every stream
    /// message. Two per-thread slots cover that pairing without locking (stream messages
    /// deserialize on pool threads, and rent/return happen synchronously inside one
    /// Deserialize call, so an array is always returned on the thread that rented it).
    /// </summary>
    internal sealed class JsonCharArrayPool : IArrayPool<char>
    {
        public static readonly JsonCharArrayPool Instance = new();

        /// <summary>Don't retain pathological buffers for the life of the thread.</summary>
        private const int MaxRetainedLength = 64 * 1024;

        [ThreadStatic] private static char[] _slot0;
        [ThreadStatic] private static char[] _slot1;

        public char[] Rent(int minimumLength)
        {
            var array = _slot0;
            if (array != null && array.Length >= minimumLength)
            {
                _slot0 = null;
                return array;
            }

            array = _slot1;
            if (array != null && array.Length >= minimumLength)
            {
                _slot1 = null;
                return array;
            }

            return new char[minimumLength];
        }

        public void Return(char[] array)
        {
            if (array == null || array.Length > MaxRetainedLength)
            {
                return;
            }

            if (_slot0 == null)
            {
                _slot0 = array;
            }
            else if (_slot1 == null)
            {
                _slot1 = array;
            }
            else if (_slot0.Length < array.Length)
            {
                _slot0 = array;
            }
            else if (_slot1.Length < array.Length)
            {
                _slot1 = array;
            }
        }
    }
}
