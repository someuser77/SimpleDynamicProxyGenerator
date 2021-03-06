﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;

namespace SimpleDynamicProxyGenerator.TypeGenerators
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
        private const string RETURN_VALUE_PROPERTY_NAME = "ReturnValue";
        
        private Type[] mConstructorArguments;

        private FieldInfo mOriginalInstanceField;

        private FieldInfo mInterceptorsMapField;

        public DynamicProxyTypeGenerator(string typeName, ModuleBuilder moduleBuilder, Type[] constructorArguments)
            : base(typeName, moduleBuilder)
        {
            mConstructorArguments = constructorArguments;
        }

        public override Type Create()
        {
            TypeBuilder typeBuilder = mModuleBuilder.DefineType("Dynamic" + mTypeName, typeof(TImplementation).Attributes, null, new Type[] { typeof(TInterface) });

            mOriginalInstanceField = AddOriginalInstanceTypeField(typeBuilder, typeof(TImplementation));

            mInterceptorsMapField = AddInterceptorMapField(typeBuilder);

            ImplementMethods(typeBuilder);

            ConstructorInfo dynamicTypeConstructor = AddConstructor(typeBuilder, typeof(TImplementation), mConstructorArguments, mOriginalInstanceField, mInterceptorsMapField);

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

        private void ImplementMethods(TypeBuilder typeBuilder)
        {
            foreach (MethodInfo method in typeof(TImplementation).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.DeclaringType != typeof(TImplementation)) continue;

                // proceedMethod.GetCustomAttributes is avoided because it creates instances of the attributes and we only
                // need to know their presence, there is no need to create them now.

                IList<CustomAttributeData> customAttributeData = method.GetCustomAttributesData();

                bool hasCustomInterceptors = customAttributeData.Any<CustomAttributeData>(attributeData => typeof(MethodInterceptorBase).IsAssignableFrom(attributeData.Constructor.DeclaringType));

                if (hasCustomInterceptors)
                {
                    ImplementRoutedMethodCall(typeBuilder, mOriginalInstanceField, method, mInterceptorsMapField);
                }
                else
                {
                    ImplementDirectMethodCall(typeBuilder, mOriginalInstanceField, method);
                }
            }
        }

        private void ImplementRoutedMethodCall(TypeBuilder typeBuilder, FieldInfo originalLocalTypeField, MethodInfo method, FieldInfo interceptorsMapField)
        {
            Type[] methodParameterTypes = GetArgumentsTypes(method);

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

            GetMethodInterceptorInstanceFromMap(ilGenerator, method.Name);

            GetMethodInterceptorStubInstance(ilGenerator, interceptorStubLocal, interceptorStubType, argumentContainerLocal);
            
            ilGenerator.Emit(OpCodes.Stloc, interceptorStubLocal);
            ilGenerator.Emit(OpCodes.Ldloc, interceptorStubLocal);

            ilGenerator.Emit(OpCodes.Callvirt, interceptionMethod);

            PutReturnedValueOnStack(ilGenerator, interceptorStubLocal, method.ReturnType);

            ilGenerator.Emit(OpCodes.Ret);
        }

        private void GetMethodInterceptorStubInstance(ILGenerator ilGenerator, LocalBuilder interceptorStubLocal, Type interceptorStubType, LocalBuilder argumentContainerLocal)
        {
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, mOriginalInstanceField);
            ilGenerator.Emit(OpCodes.Ldloc, argumentContainerLocal);

            ConstructorInfo interceptorStubTypeConstructor = interceptorStubType.GetConstructor(new Type[] { mOriginalInstanceField.FieldType, argumentContainerLocal.LocalType });
            
            ilGenerator.Emit(OpCodes.Newobj, interceptorStubTypeConstructor);
        }

        private void GetMethodInterceptorInstanceFromMap(ILGenerator ilGenerator, string methodName)
        {
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldfld, mInterceptorsMapField);
            ilGenerator.Emit(OpCodes.Ldstr, methodName);
            ilGenerator.Emit(OpCodes.Callvirt, typeof(IDictionary<string, MethodInterceptorBase>).GetMethod("get_Item"));
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

        private void PutReturnedValueOnStack(ILGenerator ilGenerator, LocalBuilder interceptorStubLocalVariable, Type returnType)
        {
            if (returnType != typeof(void))
            {
                ilGenerator.Emit(OpCodes.Ldloc, interceptorStubLocalVariable);

                ilGenerator.Emit(OpCodes.Callvirt, typeof(IInterceptionStub).GetMethod("get_" + RETURN_VALUE_PROPERTY_NAME));

                if (returnType.IsValueType)
                {
                    ilGenerator.Emit(OpCodes.Unbox_Any, returnType);
                }
            }
        }
    }
}
