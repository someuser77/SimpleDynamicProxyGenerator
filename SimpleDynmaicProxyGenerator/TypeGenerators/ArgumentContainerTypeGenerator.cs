using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleDynamicProxyGenerator.TypeGenerators
{
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
}
