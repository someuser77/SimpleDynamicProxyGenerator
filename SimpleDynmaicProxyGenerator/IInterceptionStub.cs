
namespace ConsoleApplication1
{
    public interface IInterceptionStub
    {
        void Proceed();
        object ReturnValue { get; set; }
    }
}
