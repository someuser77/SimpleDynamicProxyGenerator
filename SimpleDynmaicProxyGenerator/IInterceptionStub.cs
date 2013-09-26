
namespace SimpleDynamicProxyGenerator
{
    public interface IInterceptionStub
    {
        void Proceed();
        object ReturnValue { get; set; }
    }
}
