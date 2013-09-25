using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleApplication1;

namespace SimpleDynamicProxyGenerator.Test
{
    [TestClass]
    public class EmptyInterceptorsTests
    {
        [TestMethod]
        public void Test_Simple_Get_ValueType()
        {
            int expected = 9;
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, PersonWithEmptyInterceptors>(String.Empty, 9);
            
            int actual = person.GetAge();

            Assert.AreEqual<int>(expected, actual);
        }

        [TestMethod]
        public void Test_Simple_Set_ValueType()
        {
            int expected = 10;
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, PersonWithEmptyInterceptors>(String.Empty, 9);

            person.SetAge(10);

            int actual = person.GetAge();

            Assert.AreEqual<int>(expected, actual);
        }
    }

    public interface IPerson
    {
        void SetAge(int age);

        int GetAge();
    }

    public class PersonWithEmptyInterceptors : IPerson
    {
        private int mAge;
        private string mName;

        public PersonWithEmptyInterceptors(string name, int age)
        {
            mAge = age;
            mName = name;
        }

        [EmptyMethodInterceptor]
        public void SetAge(int age)
        {
            mAge = age;
        }

        [EmptyMethodInterceptor]
        public int GetAge()
        {
            return mAge;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class EmptyMethodInterceptor : Attribute
    {
        public void Intercept(IInterceptionStub stub)
        {
            stub.Proceed();
        }
    }
}
