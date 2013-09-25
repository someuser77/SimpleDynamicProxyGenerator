using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleApplication1;

namespace SimpleDynamicProxyGenerator.Test
{
    [TestClass]
    public class NoInterceptorsTests
    {
        [TestMethod]
        public void Test_No_Interception_Get_ValueType()
        {
            int expected = 9;
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, PersonWithEmptyInterceptors>(String.Empty, 9);

            int actual = person.GetAge();

            Assert.AreEqual<int>(expected, actual);
        }

        [TestMethod]
        public void Test_No_Interception_Set_ValueType()
        {
            int expected = 10;
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, PersonWithEmptyInterceptors>(String.Empty, 9);

            person.SetAge(10);

            int actual = person.GetAge();

            Assert.AreEqual<int>(expected, actual);
        }
    }

    public class PersonWithNoInterceptors : IPerson
    {
        private int mAge;
        private string mName;

        public PersonWithNoInterceptors(string name, int age)
        {
            mAge = age;
            mName = name;
        }

        public void SetAge(int age)
        {
            mAge = age;
        }

        public int GetAge()
        {
            return mAge;
        }
    }
}
