﻿namespace Suges.Template.Common
{
    public class Test : ITest
    {
        public string DoSomething(string parameter)
        {
            return $"Message from Test with { parameter }";
        }
    }
}
