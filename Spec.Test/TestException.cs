using System;

namespace Spec.Test
{
    public class TestException : Exception
    {
        public TestException(string message) : base(message) {}
    }
}