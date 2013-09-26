using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleDynamicProxyGenerator.TypeGenerators
{
    /// <summary>
    /// Generates a dynamic type to implement the IInterceptionStub interface with a Proceed()
    /// proceedMethod to call the proceedMethod on the underlying type.
    /// </summary>
    internal class IInterceptionStubImplementationTypeGenerator : TypeGenerator
    {
        private MethodInfo mMethod;

        private Type mArgumentContainerType;
        private Type mTargetInstanceType;

        private TypeBuilder mTypeBuilder;

        private FieldInfo mTargetInstance;
        private FieldInfo mArgumentContainer;

        const string RETURN_TYPE_PROPERTY_NAME = "ReturnValue";
        const string INTERCEPTION_STUB_PROCEED_METHOD_NAME = "Proceed";

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
            mTypeBuilder = mModuleBuilder.DefineType(mTypeName, TypeAttributes.Public | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit, null, new Type[] { typeof(IInterceptionStub) });

            mTargetInstance = mTypeBuilder.DefineField("mTargetInstance", mTargetInstanceType, FieldAttributes.Private);
            
            mArgumentContainer = mTypeBuilder.DefineField("mArgumentContainer", mArgumentContainerType, FieldAttributes.Private);

            AddConstructor();

            FieldBuilder propertyBackingField = AddReturnValueAutoImplementedProperty();

            ImplementProceedMethod(propertyBackingField);

            return mTypeBuilder.CreateType();
        }

        private void ImplementProceedMethod(FieldBuilder propertyBackingField)
        {
            MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final;
            CallingConventions callingConventions = CallingConventions.Standard | CallingConventions.HasThis;

            MethodBuilder proceedMethod = mTypeBuilder.DefineMethod(INTERCEPTION_STUB_PROCEED_METHOD_NAME, methodAttributes, callingConventions, null, Type.EmptyTypes);

            mTypeBuilder.DefineMethodOverride(proceedMethod, typeof(IInterceptionStub).GetMethod(INTERCEPTION_STUB_PROCEED_METHOD_NAME));

            ILGenerator ilGenerator = proceedMethod.GetILGenerator();

            LocalBuilder returnTypeLocalVariable = null;

            if (mMethod.ReturnType != typeof(void))
            {
                returnTypeLocalVariable = ilGenerator.DeclareLocal(mMethod.ReturnType);
            }

            ilGenerator.Emit(OpCodes.Ldarg_0);

            ilGenerator.Emit(OpCodes.Ldfld, mTargetInstance);

            UnpackArgumentContainerFromField(ilGenerator, mArgumentContainer, mMethod);

            ilGenerator.Emit(OpCodes.Callvirt, mMethod);

            SaveReturnValue(ilGenerator, returnTypeLocalVariable);

            ilGenerator.Emit(OpCodes.Ldarg_0);

            PrepareReturnValueForStorageInBackingField(ilGenerator, returnTypeLocalVariable);

            ilGenerator.Emit(OpCodes.Stfld, propertyBackingField);

            ilGenerator.Emit(OpCodes.Ret);
        }

        private void SaveReturnValue(ILGenerator ilGenerator, LocalBuilder returnTypeLocalVariable)
        {
            if (mMethod.ReturnType != typeof(void))
            {
                ilGenerator.Emit(OpCodes.Stloc, returnTypeLocalVariable);
            }
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

        private void AddConstructor()
        {
            ConstructorBuilder constructorBuilder = mTypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { mTargetInstanceType, mArgumentContainerType });
            ILGenerator ilGenerator = constructorBuilder.GetILGenerator();
            TypeGenerator.AddEmptyObjectConstructorCall(ilGenerator);

            StoreArgumentInClassField(ilGenerator, 1, mTargetInstance);
            StoreArgumentInClassField(ilGenerator, 2, mArgumentContainer);

            ilGenerator.Emit(OpCodes.Ret);
        }

        private FieldBuilder AddReturnValueAutoImplementedProperty()
        {
            BuiltProperty property = AddProperty(mTypeBuilder, "returnValue", RETURN_TYPE_PROPERTY_NAME, typeof(object));

            mTypeBuilder.DefineMethodOverride(property.Getter, typeof(IInterceptionStub).GetMethod("get_" + RETURN_TYPE_PROPERTY_NAME));

            mTypeBuilder.DefineMethodOverride(property.Setter, typeof(IInterceptionStub).GetMethod("set_" + RETURN_TYPE_PROPERTY_NAME));

            return property.BackingField;
        }

        private void PrepareReturnValueForStorageInBackingField(ILGenerator ilGenerator, LocalBuilder returnTypeLocalVariable)
        {
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
        }
    }
}
