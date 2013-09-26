using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleDynamicProxyGenerator.TypeGenerators
{
    /// <summary>
    /// Generates a dynamic type to hold the arguments for a method.
    /// The dynamic type has a public field for each method parameter.
    /// </summary>
    internal class ArgumentContainerTypeGenerator : TypeGenerator
    {
        private MethodBase mMethod;

        private static string GetArgumentContainerTypeName(MethodBase method)
        {
            return method.DeclaringType.FullName + "." + method.Name + "ArgumentContainer";
        }

        public ArgumentContainerTypeGenerator(ModuleBuilder moduleBuilder, MethodBase method)
            : base(GetArgumentContainerTypeName(method), moduleBuilder)
        {
            mMethod = method;
        }

        public override Type Create()
        {
            TypeBuilder typeBuilder = mModuleBuilder.DefineType(mTypeName, TypeAttributes.Public | TypeAttributes.Class);

            typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

            foreach (ParameterInfo parameter in mMethod.GetParameters())
            {
                typeBuilder.DefineField(parameter.Name, parameter.ParameterType, FieldAttributes.Public);
            }

            return typeBuilder.CreateType();
        }
    }
}
