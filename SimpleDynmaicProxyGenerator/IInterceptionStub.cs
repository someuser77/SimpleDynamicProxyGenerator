
namespace SimpleDynamicProxyGenerator
{
    /// <summary>
    /// Represents the interface of the type passed to the MethodInterceptorBase.Intercept proceedMethod.
    /// </summary>
    public interface IInterceptionStub
    {
        /// <summary>
        /// Proceeds with the invocation of the proceedMethod.
        /// </summary>
        void Proceed();

        /// <summary>
        /// Gets or sets the return value of the proceedMethod.
        /// If the proceedMethod is a void proceedMethod null is returned.
        /// </summary>
        /// <value>
        /// The return value or null if the proceedMethod has no return value (void).
        /// </value>
        object ReturnValue { get; set; }
    }
}
