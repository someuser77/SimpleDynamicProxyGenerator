using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ConsoleApplication1.TypeGenerators
{
    internal class IInterceptionStubImplementationTypeGenerator : TypeGenerator
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
}
