
namespace SimpleDynamicProxyGenerator
{
    /// <summary>
    /// Represents the interface of the type passed to the MethodInterceptorBase.Intercept method.
    /// </summary>
    public interface IInterceptionStub
    {
        /// <summary>
        /// Proceeds with the invocation of the method.
        /// </summary>
        void Proceed();

        /// <summary>
        /// Gets or sets the return value of the method.
        /// If the method is a void method null is returned.
        /// </summary>
        /// <value>
        /// The return value or null if the method has no return value (void).
        /// </value>
        object ReturnValue { get; set; }
    }
}
