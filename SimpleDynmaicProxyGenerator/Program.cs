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
            Console.WriteLine(person.GetAge());
        }
    }

    public class InterceptorFactory
    {
        internal static ModuleBuilder ModuleBuilder { get; set; }

        public static TInterface GetInstanceByConstructor<TInterface, TImplementation>(params object[] constructorArguments) where TImplementation : TInterface
        {
            string typeName = typeof(TImplementation).Name;

            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("DynamicAssemblyForIntercepting" + typeName), AssemblyBuilderAccess.Run | AssemblyBuilderAccess.Save);

            ModuleBuilder = assemblyBuilder.DefineDynamicModule("MainModule", "assembly.dll");

            DynamicProxyTypeGenerator<TInterface, TImplementation> proxyTypeGenerator = new DynamicProxyTypeGenerator<TInterface, TImplementation>("Dynamic" + typeName, ModuleBuilder, TypeGenerator.GetArgumentsTypes(constructorArguments));

            Type dynamicType = proxyTypeGenerator.Create();

            assemblyBuilder.Save("assembly.dll");
            
            Type[] dynamicTypeConstructorArgumentTupes = TypeGenerator.GetArgumentsTypes(dynamicType.GetConstructors()[0]);

            IDictionary<string, MethodInterceptorBase> interceptorsMap = GetInterceptorsMap(typeof(TImplementation));

            LambdaExpression lambda = GetInstanceCreationExpression<TInterface>(dynamicType, dynamicTypeConstructorArgumentTupes);

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

    internal abstract class TypeGenerator
    {
        protected readonly ModuleBuilder mModuleBuilder;
        protected readonly string mTypeName;
        protected static readonly MethodAttributes GetSetAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final;

        public TypeGenerator(string typeName, ModuleBuilder moduleBuilder)
        {
            mTypeName = typeName;
            mModuleBuilder = moduleBuilder;
        }

        public abstract Type Create();

        public static void AddEmptyObjectConstructorCall(ILGenerator ilGenerator)
        {
            ConstructorInfo mObjectBaseConstructor = typeof(object).GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Call, mObjectBaseConstructor);
        }

        public class PropertyBuilders
        {
            public FieldBuilder BackingField { get; set; }
            public MethodBuilder Getter { get; set; }
            public MethodBuilder Setter { get; set; }
        }

        protected PropertyBuilders AddProperty(TypeBuilder typeBuilder, string memberName, string propertyName, Type propertyType)
        {
            FieldBuilder field = typeBuilder.DefineField(memberName, propertyType, FieldAttributes.Private);

            PropertyBuilder property = typeBuilder.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, Type.EmptyTypes);

            MethodBuilder getter = AddPropertyGetter(typeBuilder, property, field);
            
            MethodBuilder setter = AddPropertySetter(typeBuilder, property, field);

            return new PropertyBuilders { BackingField = field, Getter = getter, Setter = setter };
        }

        protected MethodBuilder AddPropertyGetter(TypeBuilder typeBuilder, PropertyBuilder property, FieldBuilder backingField)
        {
            MethodBuilder getPropertyMethodBuilder = typeBuilder.DefineMethod("get_" + property.Name, GetSetAttributes, property.PropertyType, Type.EmptyTypes);

            ILGenerator ilGenerator = getPropertyMethodBuilder.GetILGenerator();

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, backingField);
            ilGenerator.Emit(OpCodes.Ret);

            property.SetGetMethod(getPropertyMethodBuilder);

            return getPropertyMethodBuilder;
        }

        protected MethodBuilder AddPropertySetter(TypeBuilder typeBuilder, PropertyBuilder property, FieldBuilder backingField)
        {
            MethodBuilder setPropertyMethodBuilder = typeBuilder.DefineMethod("set_" + property.Name, GetSetAttributes, null, new Type[] { property.PropertyType });

            ILGenerator ilGenerator = setPropertyMethodBuilder.GetILGenerator();

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);
            ilGenerator.Emit(OpCodes.Stfld, backingField);
            ilGenerator.Emit(OpCodes.Ret);

            property.SetSetMethod(setPropertyMethodBuilder);

            return setPropertyMethodBuilder;
        }

        protected FieldInfo AddInstanceField(TypeBuilder typeBuilder, Type fieldType, string fieldName)
        {
            return typeBuilder.DefineField(fieldName, fieldType, FieldAttributes.Private);
        }

        public static Type[] GetArgumentsTypes(object[] objects)
        {
            return Array.ConvertAll<object, Type>(objects, o => o.GetType());
        }

        public static Type[] GetArgumentsTypes(MethodBase method)
        {
            return GetArgumentsTypes(method.GetParameters());
        }

        public static Type[] GetArgumentsTypes(ParameterInfo[] parameters)
        {
            return Array.ConvertAll<ParameterInfo, Type>(parameters, p => p.ParameterType);
        }

        public static NewExpression GetConstructorInvocationExpression(ConstructorInfo constructorInfo)
        {
            ParameterInfo[] parametersInfo = constructorInfo.GetParameters();

            ParameterExpression[] parameters = new ParameterExpression[parametersInfo.Length];

            for (int i = 0; i < parametersInfo.Length; i++)
            {
                parameters[i] = Expression.Parameter(parametersInfo[i].ParameterType, parametersInfo[i].Name);
            }

            return Expression.New(constructorInfo, parameters);
        }

        /// <summary>
        /// Stores the argument in the given position in given class field.
        /// </summary>
        /// <param name="ilGenerator">The il generator.</param>
        /// <param name="argumentPosition">The 1 based method argument position.</param>
        /// <param name="field">The field where the argument should be stored.</param>
        public void StoreArgumentInClassField(ILGenerator ilGenerator, int argumentPosition, FieldInfo field)
        {
            ilGenerator.Emit(OpCodes.Ldarg_0);
            // can be optimised with Ldarg_S in the future
            ilGenerator.Emit(OpCodes.Ldarg, argumentPosition);
            ilGenerator.Emit(OpCodes.Stfld, field);
        }
    }

    internal abstract class DynamicProxyTypeGenerator : TypeGenerator
    {
        internal static readonly Type InterceptorsMapType = typeof(IDictionary<string, MethodInterceptorBase>);

        public DynamicProxyTypeGenerator(string typeName, ModuleBuilder moduleBuilder)
            : base(typeName, moduleBuilder) { }
    }

    internal class DynamicProxyTypeGenerator<TInterface, TImplementation> : DynamicProxyTypeGenerator where TImplementation : TInterface
    {
        private const string IMPLEMENTATION_INSTANCE_FIELD_NAME = "mOriginalInsatcne";
        private const string INTERCEPTOR_MAP_FIELD_NAME = "mInterceptorsMap";
        
        private Type[] mConstructorArguments;

        public DynamicProxyTypeGenerator(string typeName, ModuleBuilder moduleBuilder, Type[] constructorArguments)
            : base(typeName, moduleBuilder)
        {
            mConstructorArguments = constructorArguments;
        }

        public override Type Create()
        {
            TypeBuilder typeBuilder = mModuleBuilder.DefineType("Dynamic" + mTypeName, typeof(TImplementation).Attributes, null, new Type[] { typeof(TInterface) });

            FieldInfo originalInstanceField = AddOriginalInstanceTypeField(typeBuilder, typeof(TImplementation));

            FieldInfo interceptorsMapField = AddInterceptorMapField(typeBuilder);

            ImplementMethods(typeBuilder, typeof(TImplementation), originalInstanceField, interceptorsMapField);

            ConstructorInfo dynamicTypeConstructor = AddConstructor(typeBuilder, typeof(TImplementation), mConstructorArguments, originalInstanceField, interceptorsMapField);
            
            return typeBuilder.CreateType();
        }

        private FieldInfo AddOriginalInstanceTypeField(TypeBuilder typeBuilder, Type fieldType)
        {
            return AddInstanceField(typeBuilder, fieldType, IMPLEMENTATION_INSTANCE_FIELD_NAME);
        }

        private FieldInfo AddInterceptorMapField(TypeBuilder typeBuilder)
        {
            return AddInstanceField(typeBuilder, InterceptorsMapType, INTERCEPTOR_MAP_FIELD_NAME);
        }

        private ConstructorInfo AddConstructor(TypeBuilder typeBuilder, Type type, Type[] constructorArgumentTypes, FieldInfo originalInstanceField, FieldInfo interceptorMapField)
        {
            ConstructorInfo constructorInfo = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, constructorArgumentTypes, null);

            Type[] constructorArgumentTypesWithInterceptorMapType = new Type[constructorArgumentTypes.Length + 1];
            constructorArgumentTypesWithInterceptorMapType[0] = InterceptorsMapType;
            Array.Copy(constructorArgumentTypes, 0, constructorArgumentTypesWithInterceptorMapType, 1, constructorArgumentTypes.Length);

            ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(constructorInfo.Attributes, CallingConventions.Standard, constructorArgumentTypesWithInterceptorMapType);

            // define the interceptor map parameter as the first one in case we will need to support 'params' type that has to be last.
            constructorBuilder.DefineParameter(1, ParameterAttributes.None, "interceptorsMap");

            int firstParameterIndex = 2;

            ParameterInfo[] parameters = constructorInfo.GetParameters();
            
            foreach (ParameterInfo parameter in parameters)
            {
                constructorBuilder.DefineParameter(parameter.Position + firstParameterIndex, parameter.Attributes, parameter.Name);
            }

            ILGenerator ilGenerator = constructorBuilder.GetILGenerator();

            TypeGenerator.AddEmptyObjectConstructorCall(ilGenerator);

            ilGenerator.Emit(OpCodes.Ldarg_0);

            CreateInstanceOfOriginalClass(ilGenerator, constructorInfo, firstParameterIndex);

            ilGenerator.Emit(OpCodes.Stfld, originalInstanceField);

            StoreArgumentInClassField(ilGenerator, 1, interceptorMapField);

            ilGenerator.Emit(OpCodes.Ret);

            return constructorBuilder;
        }

        private void CreateInstanceOfOriginalClass(ILGenerator ilGenerator, ConstructorInfo ctor, int firstArgumentIndex)
        {
            ParameterInfo[] parameters = ctor.GetParameters();

            for (int i = 0; i < parameters.Length; i++)
            {
                ilGenerator.Emit(OpCodes.Ldarg, i + firstArgumentIndex);
            }

            ilGenerator.Emit(OpCodes.Newobj, ctor);
        }

        private void ImplementMethods(TypeBuilder typeBuilder, Type originalType, FieldInfo originalLocalTypeField, FieldInfo interceptorsMapField)
        {
            foreach (MethodInfo method in originalType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.DeclaringType != originalType) continue;

                // method.GetCustomAttributes is avoided because it creates instances of the attributes and we only
                // need to know their presence, there is no need to create them now.

                IList<CustomAttributeData> customAttributeData = method.GetCustomAttributesData();

                bool hasCustomInterceptors = customAttributeData.Any<CustomAttributeData>(attributeData => typeof(MethodInterceptorBase).IsAssignableFrom(attributeData.Constructor.DeclaringType));

                if (hasCustomInterceptors)
                {
                    ImplementRoutedMethodCall(typeBuilder, originalLocalTypeField, method, interceptorsMapField);
                }
                else
                {
                    ImplementDirectMethodCall(typeBuilder, originalLocalTypeField, method);
                }
            }
        }

        private void ImplementRoutedMethodCall(TypeBuilder typeBuilder, FieldInfo originalLocalTypeField, MethodInfo method, FieldInfo interceptorsMapField)
        {
            ParameterInfo[] methodParameters = method.GetParameters();
            Type[] methodParameterTypes = Array.ConvertAll<ParameterInfo, Type>(methodParameters, p => p.ParameterType);

            MethodBuilder methodBuilder = typeBuilder.DefineMethod(method.Name, method.Attributes, method.ReturnType, methodParameterTypes);
            ArgumentContainerTypeGenerator argumentContainerTypeGenerator = new ArgumentContainerTypeGenerator(mModuleBuilder, method);
            Type argumentContainerType = argumentContainerTypeGenerator.Create();

            IInterceptionStubImplementationTypeGenerator interceptionStubTypeGenerator = new IInterceptionStubImplementationTypeGenerator(method, originalLocalTypeField.FieldType, argumentContainerType, mModuleBuilder);
            Type interceptorStubType = interceptionStubTypeGenerator.Create();

            MethodInfo interceptionMethod = typeof(MethodInterceptorBase).GetMethod("Intercept");

            ILGenerator ilGenerator = methodBuilder.GetILGenerator();

            LocalBuilder argumentContainerLocal = ilGenerator.DeclareLocal(argumentContainerType);
            LocalBuilder interceptorStubLocal = ilGenerator.DeclareLocal(typeof(IInterceptionStub));

            PackArgumentContainerToLocal(ilGenerator, argumentContainerLocal, method);

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, interceptorsMapField);
            ilGenerator.Emit(OpCodes.Ldstr, method.Name);
            ilGenerator.Emit(OpCodes.Callvirt, typeof(IDictionary<string, MethodInterceptorBase>).GetMethod("get_Item"));

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, originalLocalTypeField);

            ilGenerator.Emit(OpCodes.Ldloc, argumentContainerLocal);


            ConstructorInfo interceptorStubTypeConstructor = interceptorStubType.GetConstructor(new Type[] { originalLocalTypeField.FieldType, argumentContainerType });
            ilGenerator.Emit(OpCodes.Newobj, interceptorStubTypeConstructor);
            ilGenerator.Emit(OpCodes.Stloc, interceptorStubLocal);
            ilGenerator.Emit(OpCodes.Ldloc, interceptorStubLocal);
            ilGenerator.Emit(OpCodes.Callvirt, interceptionMethod);

            PutReturnedValueOnStack(ilGenerator, interceptorStubLocal, method.ReturnType);

            ilGenerator.Emit(OpCodes.Ret);
        }

        private void PutReturnedValueOnStack(ILGenerator ilGenerator, LocalBuilder interceptorStubLocalVariable, Type returnType)
        {
            if (returnType != typeof(void))
            {
                ilGenerator.Emit(OpCodes.Ldloc, interceptorStubLocalVariable);
                
                ilGenerator.Emit(OpCodes.Callvirt, typeof(IInterceptionStub).GetMethod("get_ReturnValue"));
                
                if (returnType.IsValueType)
                {
                    ilGenerator.Emit(OpCodes.Unbox_Any, returnType);
                }
            }
        }

        private void PackArgumentContainerToLocal(ILGenerator ilGenerator, LocalBuilder argumentContainerField, MethodInfo method)
        {
            Type argumentContainerType = argumentContainerField.LocalType;
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
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(method.Name, method.Attributes, method.ReturnType, GetArgumentsTypes(parameters));
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

            PropertyBuilders property = AddReturnTypeAutoImplementedProperty(typeBuilder, typeof(object));
            FieldBuilder propertyBackingField = property.BackingField;

            typeBuilder.DefineMethodOverride(property.Getter, typeof(IInterceptionStub).GetMethod("get_ReturnValue"));
            typeBuilder.DefineMethodOverride(property.Setter, typeof(IInterceptionStub).GetMethod("set_ReturnValue"));

            MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final;
            MethodBuilder method = typeBuilder.DefineMethod("Proceed", methodAttributes, CallingConventions.Standard | CallingConventions.HasThis, null, Type.EmptyTypes);
            
            typeBuilder.DefineMethodOverride(method, typeof(IInterceptionStub).GetMethod("Proceed"));

            ilGenerator = method.GetILGenerator();

            LocalBuilder returnTypeLocalVariable = null;
            if (mMethod.ReturnType != typeof(void))
            {
                returnTypeLocalVariable = ilGenerator.DeclareLocal(mMethod.ReturnType);
            }
            
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, targetInstance);

            UnpackArgumentContainerFromField(ilGenerator, argumentContainer, mMethod);

            ilGenerator.Emit(OpCodes.Callvirt, mMethod);
            
            if (mMethod.ReturnType != typeof(void))
            {
                ilGenerator.Emit(OpCodes.Stloc, returnTypeLocalVariable);
            }

            ilGenerator.Emit(OpCodes.Ldarg_0);
            
            if (mMethod.ReturnType == typeof(void))
            {
                ilGenerator.Emit(OpCodes.Ldnull);
            }
            else
            {
                ilGenerator.Emit(OpCodes.Ldloc, returnTypeLocalVariable);

                if (mMethod.ReturnType.IsValueType)
                {
                    ilGenerator.Emit(OpCodes.Box, mMethod.ReturnType);
                }
            }

            ilGenerator.Emit(OpCodes.Stfld, propertyBackingField);
            
            ilGenerator.Emit(OpCodes.Ret);
            
            Type type = typeBuilder.CreateType();
            return type;
        }

        private void UnpackArgumentContainerFromField(ILGenerator ilGenerator, FieldInfo argumentContainerField, MethodInfo method)
        {
            ParameterInfo[] args = method.GetParameters();
            foreach (ParameterInfo parameter in args)
            {
                ilGenerator.Emit(OpCodes.Ldarg_0);
                ilGenerator.Emit(OpCodes.Ldfld, argumentContainerField);
                ilGenerator.Emit(OpCodes.Ldfld, mArgumentContainerType.GetField(parameter.Name));
            }
        }

        private void AddConstructor(TypeBuilder typeBuilder, FieldInfo targetInstance, FieldInfo argumentContainer)
        {
            ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { mTargetInstanceType, mArgumentContainerType });
            ILGenerator ilGenerator = constructorBuilder.GetILGenerator();
            TypeGenerator.AddEmptyObjectConstructorCall(ilGenerator);

            StoreArgumentInClassField(ilGenerator, 1, targetInstance);
            StoreArgumentInClassField(ilGenerator, 2, argumentContainer);

            ilGenerator.Emit(OpCodes.Ret);
        }

        private PropertyBuilders AddReturnTypeAutoImplementedProperty(TypeBuilder typeBuilder, Type propertyType)
        {
            return AddProperty(typeBuilder, "returnValue", "ReturnValue", propertyType);
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
        IDictionary<string, Func<int, bool>> dic = new Dictionary<string, Func<int, bool>>();

        public int Age { get; set; }

        public AnotherPerson(Person person)
        {
            _person = person;

            Console.WriteLine(dic["10"](2));

            int xxx = (int)person.GetBoxedAge();
            Console.WriteLine(xxx);
        }

        public void SetAge(int age)
        {
            _person.SetAge(a.a + a.b + a.c);
            _person.SetAge(a.a + a.b + a.c);
            _person.SetAge(a.a + a.b + a.c);
            Age = age;
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

        [MethodInterceptorA]
        public void SetAge(int age)
        {
            mAge = age;
        }

        [MethodInterceptorA]
        public int GetAge()
        {
            return mAge;
        }

        public object GetBoxedAge()
        {
            return mAge;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public abstract class MethodInterceptorBase : Attribute
    {
        public abstract void Intercept(IInterceptionStub stub);
    }
    
    public class MethodInterceptorA : MethodInterceptorBase
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

    public interface IInterceptionStub
    {
        void Proceed();
        object ReturnValue { get; set; }
    }
}
