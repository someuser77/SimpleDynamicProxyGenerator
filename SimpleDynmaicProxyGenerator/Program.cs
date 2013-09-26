using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Reflection;
using SimpleDynamicProxyGenerator.TypeGenerators;

namespace SimpleDynamicProxyGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, Person>("jhon", 9);
            person.SetAge(10);
            Console.WriteLine(person.GetAge());
        }
    }

    public interface IPerson
    {
        void SetAge(int age);

        int GetAge();
    }

    public class Person : IPerson
    {
        private int mAge;
        private string mName;

        public Person(string name, int age)
        {
            mAge = age;
            mName = name;
        }

        [MethodInterceptor]
        public void SetAge(int age)
        {
            mAge = age;
        }

        [MethodInterceptor]
        public int GetAge()
        {
            return mAge;
        }

        public object GetBoxedAge()
        {
            return mAge;
        }
    }


    
    public class MethodInterceptor : MethodInterceptorBase
    {
        public override void Intercept(IInterceptionStub stub)
        {
            Console.WriteLine("Before method!!!");
            stub.Proceed();
            if (stub.ReturnValue != null)
            {
                stub.ReturnValue = (int)stub.ReturnValue + 1;
            }
            Console.WriteLine("After method!!!");
        }
    }
}
