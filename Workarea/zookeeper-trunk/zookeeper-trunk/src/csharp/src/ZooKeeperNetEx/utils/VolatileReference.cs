﻿using System.Threading;

namespace ZooKeeperNetEx.utils
{
    internal class VolatileReference<T> where T : class
    {
        public VolatileReference(T value)
        {
            Value = value;
        }

        private T m_Value;

        public T Value
        {
            get => Volatile.Read(ref m_Value);
            set => Volatile.Write(ref m_Value, value);
        }

        public T CompareExchange(T newValue, T preconditionValue)
        {
            return Interlocked.CompareExchange(ref m_Value, newValue, preconditionValue);
        }
    }
}
