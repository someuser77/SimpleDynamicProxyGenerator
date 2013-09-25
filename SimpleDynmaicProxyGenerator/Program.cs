using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Reflection;

namespace ConsoleApplication1
{
    class Program
    {
        static void Main(string[] args)
        {
            IPerson person = InterceptorFactory.GetInstanceByConstructor<IPerson, Person>("jhon", 9);
            person.SetAge(10);
            person.GetAge();
        }
    }

    public class InterceptorFactory
    {
        private const string IMPLEMENTATION_INSTANCE_FIELD_NAME = "mOriginalInsatcne";

        internal static ModuleBuilder ModuleBuilder { get; set; }

        public static TInterface GetInstanceByConstructor<TInterface, TImplementation>(params object[] constructorArguments) where TImplementation : TInterface
        {
            string typeName = typeof(TImplementation).Name;

            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("DynamicAssemblyForIntercepting" + typeName), AssemblyBuilderAccess.Run | AssemblyBuilderAccess.Save);

            ModuleBuilder = assemblyBuilder.DefineDynamicModule("MainModule", "assembly.dll");

            TypeBuilder typeBuilder = ModuleBuilder.DefineType("Dynamic" + typeName, typeof(TImplementation).Attributes, null, new Type[] { typeof(TInterface) });

            FieldInfo originalInstanceField = AddOriginalInstanceTypeField(typeBuilder, typeof(TImplementation));

            ImplementMethods(typeBuilder, typeof(TImplementation), originalInstanceField);

            AddConstructor(typeBuilder, typeof(TImplementation), constructorArguments, originalInstanceField);

            Type dynamicType = typeBuilder.CreateType();
            
            assemblyBuilder.Save("assembly.dll");

            LambdaExpression lambda = GetInstanceCreationExpression<TInterface>(constructorArguments, dynamicType);
                        
            Func<object[], TInterface> ctor = (Func<object[], TInterface>)lambda.Compile();

            return ctor(constructorArguments);
        }

        private static LambdaExpression GetInstanceCreationExpression<TInterface>(object[] constructorArguments, Type dynamicType)
        {
            ConstructorInfo dynamicTypeRuntimeConstructorInfo = dynamicType.GetConstructor(GetArgumentsTypes(constructorArguments));

            NewExpression newTypeCtorExp = GetConstructorInvocationExpression(dynamicTypeRuntimeConstructorInfo);

            ParameterInfo[] parametersInfo = dynamicTypeRuntimeConstructorInfo.GetParameters();

            Expression[] argsExp = new Expression[parametersInfo.Length];

            ParameterExpression param = Expression.Parameter(typeof(object[]), "args");

            for (int i = 0; i < argsExp.Length; i++)
            {
                Expression index = Expression.Constant(i);

                Type paramType = parametersInfo[i].ParameterType;

                Expression paramAccessorExp = Expression.ArrayIndex(param, index);

                Expression paramCastExp = Expression.Convert(paramAccessorExp, paramType);

                argsExp[i] = paramCastExp;
            }

            NewExpression newExp = Expression.New(dynamicTypeRuntimeConstructorInfo, argsExp);

            LambdaExpression lambda = Expression.Lambda(typeof(Func<object[], TInterface>), newExp, param);

            return lambda;
        }

        private static ConstructorInfo LocateConstructorByArguments(Type type, object[] args)
        {
            Type[] argumentTypes = Array.ConvertAll<object, Type>(args, @object => @object.GetType());
            return type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, argumentTypes, null);
        }

        private static ConstructorInfo AddConstructor(TypeBuilder typeBuilder, Type type, object[] args, FieldInfo originalInstanceField)
        {
            Type[] constructorArgumentTypes = args.Select(x => x.GetType()).ToArray<Type>();

            ConstructorInfo constructorInfo = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, constructorArgumentTypes, null);

            ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(constructorInfo.Attributes, CallingConventions.Standard, constructorArgumentTypes);

            ParameterInfo[] parameters = constructorInfo.GetParameters();

            foreach (ParameterInfo parameter in parameters)
            {
                constructorBuilder.DefineParameter(parameter.Position + 1, parameter.Attributes, parameter.Name);
            }

            ILGenerator ilGenerator = constructorBuilder.GetILGenerator();

            TypeGenerator.AddBaseConstructorCall(ilGenerator);

            NewExpression baseTypeConstructorCall = GetConstructorInvocationExpression(constructorInfo);

            for (int i = 0; i <= parameters.Length; i++)
            {
                ilGenerator.Emit(OpCodes.Ldarg, i);
            }
            
            ilGenerator.Emit(OpCodes.Newobj, baseTypeConstructorCall.Constructor);
            ilGenerator.Emit(OpCodes.Stfld, originalInstanceField);
            ilGenerator.Emit(OpCodes.Ret);

            return constructorBuilder;
        }

        private static NewExpression GetConstructorInvocationExpression(ConstructorInfo constructorInfo)
        {
            ParameterInfo[] parametersInfo = constructorInfo.GetParameters();

            ParameterExpression[] parameters = new ParameterExpression[parametersInfo.Length];
            Expression[] arguments = new Expression[parametersInfo.Length];

            for (int i = 0; i < parametersInfo.Length; i++)
            {
                parameters[i] = Expression.Parameter(parametersInfo[i].ParameterType, parametersInfo[i].Name);
            }
            
            return Expression.New(constructorInfo, parameters);
        }

        private static FieldInfo AddOriginalInstanceTypeField(TypeBuilder typeBuilder, Type fieldType)
        {
            return AddInstanceField(typeBuilder, fieldType, IMPLEMENTATION_INSTANCE_FIELD_NAME);
        }

        private static FieldInfo AddInstanceField(TypeBuilder typeBuilder, Type fieldType, string fieldName)
        {
            return typeBuilder.DefineField(fieldName, fieldType, FieldAttributes.Private);
        }

        private static void ImplementMethods(TypeBuilder typeBuilder, Type originalType, FieldInfo originalLocalTypeField)
        {
            foreach (MethodInfo method in originalType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.DeclaringType != originalType) continue;
                
                object[] customAttributes = method.GetCustomAttributes(typeof(MethodInterceptor), false);

                if (customAttributes.Length > 0)
                {
                    ImplementRoutedMethodCall(typeBuilder, originalLocalTypeField, method);
                }
                else
                {
                    ImplementDirectMethodCall(typeBuilder, originalLocalTypeField, method);
                }
            }
        }

        private static void ImplementRoutedMethodCall(TypeBuilder typeBuilder, FieldInfo originalLocalTypeField, MethodInfo method)
        {
            ParameterInfo[] methodParameters = method.GetParameters();
            Type[] methodParameterTypes = Array.ConvertAll<ParameterInfo, Type>(methodParameters, p => p.ParameterType);

            MethodBuilder methodBuilder = typeBuilder.DefineMethod(method.Name, method.Attributes, method.ReturnType, methodParameterTypes);
            ArgumentContainerTypeGenerator argumentContainerTypeGenerator = new ArgumentContainerTypeGenerator(ModuleBuilder, method);
            Type argumentContainerType = argumentContainerTypeGenerator.Create();

            IInterceptionStubImplementationTypeGenerator interceptionStubTypeGenerator = new IInterceptionStubImplementationTypeGenerator(method, originalLocalTypeField.FieldType, argumentContainerType, ModuleBuilder);
            Type interceptorStubType = interceptionStubTypeGenerator.Create();


            MethodInfo interceptionMethod = interceptorStubType.GetMethod("Proceed");
            
            /*
            SignatureHelper signatureHelper = SignatureHelper.GetLocalVarSigHelper(typeBuilder.Module);
            signatureHelper.AddArgument(argumentContainerType);
            byte[] localsILStream = signatureHelper.GetSignature();
            */
            ILGenerator ilGenerator = methodBuilder.GetILGenerator();

            LocalBuilder argumentContainerField = ilGenerator.DeclareLocal(argumentContainerType);

            CopyParametersToArgumentContainer(ilGenerator, argumentContainerType, argumentContainerField, method);
            
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, originalLocalTypeField);

            ilGenerator.Emit(OpCodes.Ldloc, argumentContainerField);

            ConstructorInfo interceptorStubTypeConstructor = interceptorStubType.GetConstructor(new Type[] { originalLocalTypeField.FieldType, argumentContainerType });
            
            ilGenerator.Emit(OpCodes.Newobj, interceptorStubTypeConstructor);
            
            ilGenerator.Emit(OpCodes.Callvirt, interceptionMethod);

            
            

            ilGenerator.Emit(OpCodes.Pop);


            /* works */
            /*
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, originalLocalTypeField);
            
            for (int i = 1; i <= methodParameterTypes.Length; i++)
            {
                ilGenerator.Emit(OpCodes.Ldarg, i);
            }

            ilGenerator.Emit(OpCodes.Callvirt, method);
            
            */
            ilGenerator.Emit(OpCodes.Ret);
            
        }

        private static void CopyParametersToArgumentContainer(ILGenerator ilGenerator, Type argumentContainerType, LocalBuilder argumentContainerField, MethodInfo method)
        {
            ParameterInfo[] methodParameters = method.GetParameters();
            ilGenerator.Emit(OpCodes.Newobj, argumentContainerType.GetConstructor(Type.EmptyTypes));
            ilGenerator.Emit(OpCodes.Stloc, argumentContainerField);

            foreach (ParameterInfo parameter in methodParameters)
            {
                ilGenerator.Emit(OpCodes.Ldloc, argumentContainerField);
                ilGenerator.Emit(OpCodes.Ldarg, parameter.Position + 1);
                ilGenerator.Emit(OpCodes.Stfld, argumentContainerType.GetField(parameter.Name));
            }
        }

        private static void ImplementDirectMethodCall(TypeBuilder typeBuilder, FieldInfo originalLocalTypeField, MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(method.Name, method.Attributes, method.ReturnType, parameters.Select(x => x.ParameterType).ToArray());
            ILGenerator iLGenerator = methodBuilder.GetILGenerator();

            iLGenerator.Emit(OpCodes.Ldarg_0);
            iLGenerator.Emit(OpCodes.Ldfld, originalLocalTypeField);

            for (int i = 1; i <= parameters.Length; i++)
            {
                iLGenerator.Emit(OpCodes.Ldarg, i);
            }

            iLGenerator.Emit(OpCodes.Callvirt, method);
            iLGenerator.Emit(OpCodes.Ret);
        }

        private static Type[] GetArgumentsTypes(object[] objects)
        {
            return Array.ConvertAll<object, Type>(objects, o => o.GetType());
        }

        private static Type[] GetArgumentsTypes(MethodBase method)
        {
            return GetArgumentsTypes(method.GetParameters());
        }

        private static Type[] GetArgumentsTypes(ParameterInfo[] parameters)
        {
            return Array.ConvertAll<ParameterInfo, Type>(parameters, p => p.ParameterType);
        }
    }

    internal abstract class TypeGenerator
    {
        //protected readonly AssemblyBuilder mAssemblyBuilder;
        protected readonly ModuleBuilder mModuleBuilder;
        protected readonly string mTypeName;

        public TypeGenerator(string typeName, ModuleBuilder moduleBuilder)
        {
            mTypeName = typeName;
            //mAssemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(typeName + "TypesAssembly"), AssemblyBuilderAccess.Run | AssemblyBuilderAccess.Save);
            //mModuleBuilder = mAssemblyBuilder.DefineDynamicModule(typeName + "TypesModule");
            mModuleBuilder = moduleBuilder;
        }

        public abstract Type Create();

        public static void AddBaseConstructorCall(ILGenerator ilGenerator)
        {
            ConstructorInfo mObjectBaseConstructor = typeof(object).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, mObjectBaseConstructor);
        }
    }

    internal class ArgumentContainerTypeGenerator : TypeGenerator
    {
        private MethodBase mMethod;

        public static string GetArgumentContainerTypeName(MethodBase method)
        {
            return method.DeclaringType.Name + method.Name + "ArgumentContainer";
        }

        public ArgumentContainerTypeGenerator(ModuleBuilder moduleBuilder, MethodBase method)
            : base(GetArgumentContainerTypeName(method), moduleBuilder)
        {
            mMethod = method;
        }

        public override Type Create()
        {
            TypeBuilder typeBuilder = mModuleBuilder.DefineType(GetArgumentContainerTypeName(mMethod), TypeAttributes.Public | TypeAttributes.Class);
            
            typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            
            foreach (ParameterInfo parameter in mMethod.GetParameters())
            {
                typeBuilder.DefineField(parameter.Name, parameter.ParameterType, FieldAttributes.Public);
            }

            return typeBuilder.CreateType();
        }
    }

    internal class IInterceptionStubImplementationTypeGenerator :  TypeGenerator
    {
        private MethodInfo mMethod;
        private Type mArgumentContainerType;
        private Type mTargetInstanceType;

        public static string GetTypeName(MethodInfo method)
        {
            return method.DeclaringType.Name + method.Name + "InterceptionStubImplementation";
        }

        public IInterceptionStubImplementationTypeGenerator(MethodInfo method, Type targetInstanceType, Type argumentContainerType, ModuleBuilder moduleBuilder)
            : base(GetTypeName(method), moduleBuilder)
        {
            mMethod = method;
            mTargetInstanceType = targetInstanceType;
            mArgumentContainerType = argumentContainerType;
        }

        public override Type Create()
        {
            ILGenerator ilGenerator;
            TypeBuilder typeBuilder = mModuleBuilder.DefineType(GetTypeName(mMethod), TypeAttributes.Public | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit, null, new Type[] { typeof(IInterceptionStub) });
            
            FieldInfo targetInstance = typeBuilder.DefineField("mTargetInstance", mTargetInstanceType, FieldAttributes.Private);
            FieldInfo argumentContainer = typeBuilder.DefineField("mArgumentContainer", mArgumentContainerType, FieldAttributes.Private);
            
            AddConstructor(typeBuilder, targetInstance, argumentContainer);


            MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final;
            MethodBuilder method = typeBuilder.DefineMethod("Proceed", methodAttributes, CallingConventions.Standard | CallingConventions.HasThis, typeof(object), Type.EmptyTypes);
            
            typeBuilder.DefineMethodOverride(method, typeof(IInterceptionStub).GetMethod("Proceed"));

            ilGenerator = method.GetILGenerator();
            
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, targetInstance);

            ParameterInfo[] args = mMethod.GetParameters();
            foreach (ParameterInfo parameter in args)
            {
                //ilGenerator.Emit(OpCodes.Ldc_I4_1);
                
                ilGenerator.Emit(OpCodes.Ldarg_0);
                ilGenerator.Emit(OpCodes.Ldfld, argumentContainer);
                ilGenerator.Emit(OpCodes.Ldfld, mArgumentContainerType.GetField(parameter.Name));
            }

            ilGenerator.Emit(OpCodes.Callvirt, mMethod);

            ilGenerator.Emit(OpCodes.Ldnull);
            ilGenerator.Emit(OpCodes.Ret);
            
            Type type = typeBuilder.CreateType();
            return type;
        }

        private void AddConstructor(TypeBuilder typeBuilder, FieldInfo targetInstance, FieldInfo argumentContainer)
        {
            ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { mTargetInstanceType, mArgumentContainerType });
            ILGenerator ilGenerator = constructorBuilder.GetILGenerator();
            TypeGenerator.AddBaseConstructorCall(ilGenerator);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);
            ilGenerator.Emit(OpCodes.Stfld, targetInstance);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_2);
            ilGenerator.Emit(OpCodes.Stfld, argumentContainer);
            ilGenerator.Emit(OpCodes.Ret);
        }

    }

    public interface IPerson
    {
        void SetAge(int age);

        int GetAge();
    }

    public class Containter
    {
        public int a;
        public int b;
        public int c;
    }

    public class AnotherPerson : IPerson
    {
        Person _person;
        Containter a = new Containter();

        public AnotherPerson(Person person)
        {
            _person = person;
            a.a = 9;
            a.b = 9;
            a.c = 9;
        }

        public void SetAge(int age)
        {
            _person.SetAge(a.a + a.b + a.c);
            _person.SetAge(a.a + a.b + a.c);
            _person.SetAge(a.a + a.b + a.c);
        }

        public int GetAge()
        {
            throw new NotImplementedException();
        }
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

        [MethodInterceptor("111")]
        public void SetAge(int age)
        {
            mAge = age;
        }

        public int GetAge()
        {
            return mAge;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MethodInterceptor : Attribute
    {
        private string mMessage;
        
        public MethodInterceptor(string message)
        {
            mMessage = message;
        }

        public void Intercept(IInterceptionStub stub)
        {
            Console.WriteLine(mMessage);
            stub.Proceed();
            Console.WriteLine(mMessage);
        }
    }

    public interface IInterceptionStub
    {
        object Proceed();
    }
}
