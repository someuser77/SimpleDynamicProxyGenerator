using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using SimpleDynamicProxyGenerator.TypeGenerators;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleDynamicProxyGenerator
{
    public class InterceptorFactory
    {
        public static TInterface GetInstanceByConstructor<TInterface, TImplementation>(params object[] constructorArguments) where TImplementation : TInterface
        {
            string typeName = typeof(TImplementation).Name;

            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("DynamicAssemblyForIntercepting" + typeName), AssemblyBuilderAccess.Run | AssemblyBuilderAccess.Save);

            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule", "assembly.dll");

            DynamicProxyTypeGenerator<TInterface, TImplementation> proxyTypeGenerator = new DynamicProxyTypeGenerator<TInterface, TImplementation>("Dynamic" + typeName, moduleBuilder, TypeGenerator.GetArgumentsTypes(constructorArguments));

            Type dynamicType = proxyTypeGenerator.Create();

            assemblyBuilder.Save("assembly.dll");

            Type[] dynamicTypeConstructorArgumentTypes = TypeGenerator.GetArgumentsTypes(dynamicType.GetConstructors()[0]);

            IDictionary<string, MethodInterceptorBase> interceptorsMap = GetInterceptorsMap(typeof(TImplementation));

            LambdaExpression lambda = GetInstanceCreationExpression<TInterface>(dynamicType, dynamicTypeConstructorArgumentTypes);

            Func<IDictionary<string, MethodInterceptorBase>, object[], TInterface> ctor = (Func<IDictionary<string, MethodInterceptorBase>, object[], TInterface>)lambda.Compile();

            return ctor(interceptorsMap, constructorArguments);
        }

        private static IDictionary<string, MethodInterceptorBase> GetInterceptorsMap(Type type)
        {
            IDictionary<string, MethodInterceptorBase> interceptorsMap = new Dictionary<string, MethodInterceptorBase>();

            foreach (MethodInfo methodInfo in type.GetMethods())
            {
                object[] attributes = methodInfo.GetCustomAttributes(typeof(MethodInterceptorBase), false);
                if (attributes.Length > 0)
                {
                    interceptorsMap.Add(methodInfo.Name, (MethodInterceptorBase)attributes[0]);
                }
            }

            return interceptorsMap;
        }

        private static LambdaExpression GetInstanceCreationExpression<TInterface>(Type dynamicType, Type[] constructorArgumentTypes)
        {
            ConstructorInfo dynamicTypeRuntimeConstructorInfo = dynamicType.GetConstructor(constructorArgumentTypes);

            NewExpression newTypeCtorExp = TypeGenerator.GetConstructorInvocationExpression(dynamicTypeRuntimeConstructorInfo);

            ParameterExpression interceptorsMapParameter = Expression.Parameter(DynamicProxyTypeGenerator.InterceptorsMapType, "interceptorsMap");

            ParameterInfo[] parametersInfo = dynamicTypeRuntimeConstructorInfo.GetParameters();

            Expression[] argsExp = new Expression[parametersInfo.Length];

            ParameterExpression args = Expression.Parameter(typeof(object[]), "constructorArgumentTypes");

            argsExp[0] = interceptorsMapParameter;

            for (int i = 1; i < argsExp.Length; i++)
            {
                Expression index = Expression.Constant(i - 1);

                Type paramType = parametersInfo[i].ParameterType;

                Expression paramAccessorExp = Expression.ArrayIndex(args, index);

                Expression paramCastExp = Expression.Convert(paramAccessorExp, paramType);

                argsExp[i] = paramCastExp;
            }

            NewExpression newExp = Expression.New(dynamicTypeRuntimeConstructorInfo, argsExp);

            LambdaExpression lambda = Expression.Lambda(typeof(Func<IDictionary<string, MethodInterceptorBase>, object[], TInterface>), newExp, interceptorsMapParameter, args);

            return lambda;
        }

        private static ConstructorInfo LocateConstructorByArguments(Type type, object[] args)
        {
            Type[] argumentTypes = Array.ConvertAll<object, Type>(args, @object => @object.GetType());
            return type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, argumentTypes, null);
        }
    }
}
