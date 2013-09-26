using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleDynamicProxyGenerator;

namespace SimpleDynamicProxyGenerator.Test
{
    [TestClass]
    public class EmptyInterceptorsTests
    {
        [TestInitialize]
        public void InitializeTest()
        {
            SharedFlag.FlagBeforeCall = false;
            SharedFlag.FlagAfterCall = false;
        }

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

        [TestMethod]
        public void Test_Interception_Was_Called()
        {
            SharedFlag.FlagBeforeCall = false;
            SharedFlag.FlagAfterCall = false;
            
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, PersonWithSharedFlagInterceptors>(String.Empty, 9);

            person.SetAge(10);

            Assert.AreEqual<bool>(true, SharedFlag.FlagBeforeCall, "The Interceptor wasn't called.");
            Assert.AreEqual<bool>(true, SharedFlag.FlagAfterCall, "The Interceptor didn't return.");
        }

    }

    internal static class SharedFlag
    {
        public static bool FlagBeforeCall { get; set; }
        public static bool FlagAfterCall { get; set; }
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

    public class PersonWithSharedFlagInterceptors : IPerson
    {
        private int mAge;
        private string mName;

        public PersonWithSharedFlagInterceptors(string name, int age)
        {
            mAge = age;
            mName = name;
        }

        [SharedFlagSetterMethodInterceptor]
        public void SetAge(int age)
        {
            mAge = age;
        }

        [SharedFlagSetterMethodInterceptor]
        public int GetAge()
        {
            return mAge;
        }
    }

    public class EmptyMethodInterceptor : MethodInterceptorBase
    {
        public override void Intercept(IInterceptionStub stub)
        {
            stub.Proceed();
        }
    }

    public class SharedFlagSetterMethodInterceptor : MethodInterceptorBase
    {
        public override void Intercept(IInterceptionStub stub)
        {
            SharedFlag.FlagBeforeCall = true;
            stub.Proceed();
            SharedFlag.FlagAfterCall = true;
        }
    }
}
