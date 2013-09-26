using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SimpleDynamicProxyGenerator
{
    /// <summary>
    /// The base class to be used when implementing interceptors.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public abstract class MethodInterceptorBase : Attribute
    {
        /// <summary>
        /// The proceedMethod that will be called when intercepting the proceedMethod on which the attribute was set.
        /// </summary>
        /// <param name="stub">A dynamic class upon which to call Proceed in order to invoke the proceedMethod from the underlying instance.</param>
        public abstract void Intercept(IInterceptionStub stub);
    }
}
