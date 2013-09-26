using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;

namespace ConsoleApplication1.TypeGenerators
{
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
}
