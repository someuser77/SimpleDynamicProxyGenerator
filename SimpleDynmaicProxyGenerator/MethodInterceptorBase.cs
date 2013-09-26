using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ConsoleApplication1
{
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class MethodInterceptorBase : Attribute
    {
        public abstract void Intercept(IInterceptionStub stub);
    }
}
